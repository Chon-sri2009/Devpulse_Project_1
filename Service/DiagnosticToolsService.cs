using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record LoadTestResult(int Requested, int Succeeded, int Failed, double AverageMs, long MinMs, long MaxMs, int DurationMs);
public sealed record ProcessInfo(int Id, string Name, double MemoryMb, double CpuSeconds, int Threads);
public sealed record LogSnapshot(string File, IReadOnlyList<string> Lines, long Length, DateTimeOffset ReadAt, string? Error);
public sealed record FileCryptoResult(string FileName, byte[] Contents);

public sealed class DiagnosticToolsService(OperationsSettings settings, IHttpClientFactory clients)
{
    public const int MaxCryptoFileBytes = 25 * 1024 * 1024;
    public const int MaxEncryptedPackageBytes = MaxCryptoFileBytes + 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 210_000;
    private const int MaxFileNameBytes = 240;
    private static readonly byte[] EncryptionMagic = "DPENC001"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public IReadOnlyList<string> ApprovedUrls => settings.ApprovedUrls.Where(x => Uri.TryCreate(x, UriKind.Absolute, out var u)
        && u.Scheme is "http" or "https" && string.IsNullOrEmpty(u.UserInfo)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> ApprovedLogs => settings.ApprovedLogFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<string> FetchJsonAsync(string url, CancellationToken ct)
    {
        const int maxBytes = 2_000_000;
        EnsureUrl(url);
        using var response = await clients.CreateClient("Diagnostics").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidOperationException("JSON response exceeds the 2 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var limited = new MemoryStream(Math.Min(maxBytes, (int)(response.Content.Headers.ContentLength ?? 0)));
        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new InvalidOperationException("JSON response exceeds the 2 MB limit.");
            await limited.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        limited.Position = 0;
        using var document = JsonDocument.Parse(limited.ToArray());
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<LoadTestResult> LoadTestAsync(string url, int requests, int concurrency, CancellationToken ct)
    {
        EnsureUrl(url);
        requests = Math.Clamp(requests, 1, Math.Max(1, settings.LoadTestMaxRequests));
        concurrency = Math.Clamp(concurrency, 1, Math.Min(requests, Math.Max(1, settings.LoadTestMaxConcurrency)));
        var timings = new System.Collections.Concurrent.ConcurrentBag<long>(); int succeeded = 0, failed = 0;
        var total = Stopwatch.StartNew(); using var gate = new SemaphoreSlim(concurrency);
        var tasks = Enumerable.Range(0, requests).Select(async _ =>
        {
            await gate.WaitAsync(ct); var watch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("DevPulse-LoadTest/1.0");
                using var response = await clients.CreateClient("LoadTest").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode) Interlocked.Increment(ref succeeded); else Interlocked.Increment(ref failed);
            }
            catch (HttpRequestException) { Interlocked.Increment(ref failed); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Interlocked.Increment(ref failed); }
            finally { timings.Add(watch.ElapsedMilliseconds); gate.Release(); }
        });
        await Task.WhenAll(tasks);
        return new(requests, succeeded, failed, timings.Average(), timings.Min(), timings.Max(), (int)total.ElapsedMilliseconds);
    }

    public LogSnapshot ReadLog(string file)
    {
        var full = Path.GetFullPath(file);
        if (!ApprovedLogs.Contains(full, StringComparer.OrdinalIgnoreCase)) return new(full, [], 0, DateTimeOffset.UtcNow, "Log file is not allowlisted.");
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - 128 * 1024); stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream); if (start > 0) reader.ReadLine();
            var lines = new Queue<string>(); string? line;
            while ((line = reader.ReadLine()) is not null) { lines.Enqueue(line); while (lines.Count > 200) lines.Dequeue(); }
            return new(full, lines.ToArray(), stream.Length, DateTimeOffset.UtcNow, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(full, [], 0, DateTimeOffset.UtcNow, "The log file cannot be read."); }
    }

    public IReadOnlyList<ProcessInfo> GetProcesses() => Process.GetProcesses().Select(process =>
    {
        try
        {
            using (process)
                return new ProcessInfo(process.Id, process.ProcessName,
                    Math.Round(process.WorkingSet64 / 1048576d, 1), Math.Round(process.TotalProcessorTime.TotalSeconds, 1), process.Threads.Count);
        }
        catch { process.Dispose(); return null; }
    }).Where(x => x is not null).OrderByDescending(x => x!.MemoryMb).Take(50).Cast<ProcessInfo>().ToArray();

    public static async Task<string> Sha256Async(Stream stream, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static async Task<FileCryptoResult> EncryptFileAsync(Stream stream, string fileName, string passphrase, CancellationToken ct)
    {
        ValidatePassphrase(passphrase);
        var safeName = SafeFileName(fileName);
        var fileNameBytes = StrictUtf8.GetBytes(safeName);
        if (fileNameBytes.Length > MaxFileNameBytes)
            throw new InvalidOperationException($"The file name must be no more than {MaxFileNameBytes} UTF-8 bytes.");

        var plaintext = await ReadBoundedAsync(stream, MaxCryptoFileBytes, "File exceeds the 25 MB encryption limit.", ct);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        var headerLength = EncryptionMagic.Length + SaltSize + NonceSize + sizeof(ushort) + fileNameBytes.Length;
        var package = new byte[headerLength + TagSize + plaintext.Length];

        try
        {
            var offset = 0;
            EncryptionMagic.CopyTo(package, offset); offset += EncryptionMagic.Length;
            salt.CopyTo(package, offset); offset += SaltSize;
            nonce.CopyTo(package, offset); offset += NonceSize;
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(offset, sizeof(ushort)), (ushort)fileNameBytes.Length); offset += sizeof(ushort);
            fileNameBytes.CopyTo(package, offset);

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                nonce,
                plaintext,
                package.AsSpan(headerLength + TagSize),
                package.AsSpan(headerLength, TagSize),
                package.AsSpan(0, headerLength));
            return new($"{safeName}.devpulse", package);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static async Task<FileCryptoResult> DecryptFileAsync(Stream stream, string passphrase, CancellationToken ct)
    {
        ValidatePassphrase(passphrase);
        var package = await ReadBoundedAsync(stream, MaxEncryptedPackageBytes, "Encrypted package exceeds the 25 MB file limit.", ct);
        var minimumLength = EncryptionMagic.Length + SaltSize + NonceSize + sizeof(ushort) + 1 + TagSize;
        if (package.Length < minimumLength || !package.AsSpan(0, EncryptionMagic.Length).SequenceEqual(EncryptionMagic))
            throw new InvalidOperationException("This is not a supported DevPulse encrypted file.");

        var offset = EncryptionMagic.Length;
        var salt = package.AsSpan(offset, SaltSize); offset += SaltSize;
        var nonce = package.AsSpan(offset, NonceSize); offset += NonceSize;
        var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset, sizeof(ushort))); offset += sizeof(ushort);
        if (fileNameLength is 0 or > MaxFileNameBytes || offset + fileNameLength + TagSize > package.Length)
            throw new InvalidOperationException("The encrypted file header is invalid.");

        string fileName;
        try { fileName = StrictUtf8.GetString(package, offset, fileNameLength); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("The encrypted file header is invalid."); }
        offset += fileNameLength;
        var ciphertextLength = package.Length - offset - TagSize;
        if (ciphertextLength > MaxCryptoFileBytes)
            throw new InvalidOperationException("Encrypted package exceeds the 25 MB file limit.");

        var plaintext = new byte[ciphertextLength];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                nonce,
                package.AsSpan(offset + TagSize, ciphertextLength),
                package.AsSpan(offset, TagSize),
                plaintext,
                package.AsSpan(0, offset));
            return new(SafeFileName(fileName), plaintext);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidOperationException("The password is incorrect or the encrypted file has been changed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static (string Header, string Payload, string? Error) DecodeJwt(string token)
    {
        if (token.Length > 65_536) return ("", "", "JWT exceeds the 64 KB limit.");
        try
        {
            var parts = token.Trim().Split('.'); if (parts.Length < 2) return ("", "", "JWT must contain at least two segments.");
            return (Pretty(Base64Url(parts[0])), Pretty(Base64Url(parts[1])), null);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        { return ("", "", "JWT header or payload is not valid Base64URL JSON."); }
    }

    private static byte[] Base64Url(string value)
    { value = value.Replace('-', '+').Replace('_', '/'); value += new string('=', (4 - value.Length % 4) % 4); return Convert.FromBase64String(value); }
    private static string Pretty(byte[] value) { using var json = JsonDocument.Parse(value); return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
    private static void ValidatePassphrase(string passphrase)
    {
        if (passphrase.Length is < 12 or > 1024)
            throw new InvalidOperationException("Password must be between 12 and 1,024 characters.");
    }
    private static string SafeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..") return "decrypted-file";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(safe.Select(c => char.IsControl(c) || c is '/' or '\\' || invalid.Contains(c) ? '_' : c).ToArray());
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, string limitError, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (read > maxBytes - total) throw new InvalidOperationException(limitError);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        return output.ToArray();
    }
    private void EnsureUrl(string url) { if (!ApprovedUrls.Contains(url, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("URL is not in the owner allowlist."); }
}
