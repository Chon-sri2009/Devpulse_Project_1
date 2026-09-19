param([string]$PublishDirectory = 'artifacts/publish')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = if ([System.IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory
} else {
    Join-Path $projectRoot $PublishDirectory
}
$assembly = Join-Path $publishRoot 'MiniProject_Everything_1.dll'
if (!(Test-Path $assembly)) { throw 'Publish the app to artifacts/publish before running the smoke test.' }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$testData = Join-Path ([System.IO.Path]::GetTempPath()) ('devpulse-smoke-' + [guid]::NewGuid().ToString('N'))
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = 'dotnet'
$start.WorkingDirectory = $publishRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Arguments = '"' + $assembly.Replace('"', '\"') + '" --urls "http://127.0.0.1:' + $port + '"'
$start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
$start.EnvironmentVariables['Spotify__ClientId'] = 'smoke-test-client'
$start.EnvironmentVariables['Spotify__ClientSecret'] = 'smoke-test-secret'
$start.EnvironmentVariables['Storage__DataPath'] = $testData
$start.EnvironmentVariables['Diagnostics__Enabled'] = 'false'
$start.EnvironmentVariables['Hosting__RedirectToHttps'] = 'false'
$start.EnvironmentVariables['Admin__Username'] = 'smoke-admin'
$start.EnvironmentVariables['Admin__Password'] = 'smoke-test-password-123'
$process = [System.Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(5)
$client.BaseAddress = [uri]"http://127.0.0.1:$port"
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "Server exited: $($stderr.GetAwaiter().GetResult()) $($stdout.GetAwaiter().GetResult())" }
        try {
            $r = $client.GetAsync('/healthz').GetAwaiter().GetResult()
            $ready = $r.IsSuccessStatusCode
            $r.Dispose()
            if ($ready) { break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (!$ready) { throw 'Server did not become ready.' }
    foreach ($path in @('/', '/dashboard', '/spotify', '/healthz', '/admin')) {
        $r = $client.GetAsync($path).GetAwaiter().GetResult()
        if ([int]$r.StatusCode -ne 200) { throw "$path returned $($r.StatusCode)" }
        if ($path -eq '/') {
            $body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (!$body.Contains('Host diagnostics are disabled') -or $body.Contains('Run health check')) { throw 'Production diagnostics gate failed.' }
        }
        if ($path -eq '/admin' -and !$r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('Administrator sign in')) { throw 'Admin sign-in page failed.' }
        $r.Dispose()
        Write-Output "PASS $path"
    }
    $r = $client.GetAsync('/definitely-missing').GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 404 -or !$r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('Page not found')) { throw '404 page failed.' }
    $r.Dispose()
    Write-Output 'PASS unknown route'
    foreach ($path in @('/operations', '/telemetry', '/tools')) {
        $r = $client.GetAsync($path).GetAwaiter().GetResult()
        if ([int]$r.StatusCode -ne 404) { throw "Production diagnostics exposed $path" }
        $r.Dispose()
    }
    Write-Output 'PASS production operations gate'
    $homeResponse = $client.GetAsync('/').GetAwaiter().GetResult()
    $html = $homeResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $homeResponse.Dispose()
    $assets = @([regex]::Matches($html, 'href="([^"]+\.css)"') | ForEach-Object { $_.Groups[1].Value })
    $assets += '_framework/blazor.web.js'
    $assets += 'js/spotify-player.js'
    foreach ($asset in $assets) {
        $r = $client.GetAsync('/' + $asset.TrimStart('/')).GetAwaiter().GetResult()
        $bytes = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        if (!$r.IsSuccessStatusCode -or $bytes.Length -eq 0 -or $r.Content.Headers.ContentType.MediaType -eq 'text/html') { throw "Missing static asset: $asset" }
        $r.Dispose()
    }
    Write-Output 'PASS published CSS and Blazor scripts'
    $r = $client.GetAsync('/spotify/login').GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 302 -or !$r.Headers.Location.AbsoluteUri.StartsWith('https://accounts.spotify.com/authorize')) { throw 'Spotify challenge failed.' }
    $r.Dispose()
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, '/spotify/login')
    $request.Headers.Add('X-Forwarded-Proto', 'https')
    $r = $client.SendAsync($request).GetAwaiter().GetResult()
    $location = [uri]::UnescapeDataString($r.Headers.Location.AbsoluteUri)
    if (!$location.Contains("redirect_uri=https://127.0.0.1:$port/signin-spotify")) { throw 'Trusted-proxy callback scheme failed.' }
    if (!$location.Contains('code_challenge=')) { throw 'OAuth PKCE missing.' }
    if (!$location.Contains('streaming') -or !$location.Contains('user-read-email') -or !$location.Contains('user-library-read')) { throw 'Spotify browser/library OAuth scopes missing.' }
    $r.Dispose()
    $request.Dispose()
    Write-Output 'PASS OAuth redirect and trusted proxy'
    $r = $client.GetAsync('/spotify/browser-token').GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 401 -or $r.Headers.CacheControl.NoStore -ne $true) { throw 'Browser token endpoint did not reject an anonymous request safely.' }
    $r.Dispose()
    Write-Output 'PASS browser token endpoint rejects anonymous requests'
    $content = [System.Net.Http.StringContent]::new('')
    $r = $client.PostAsync('/spotify/logout', $content).GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 400) { throw 'Logout accepted missing antiforgery token.' }
    $r.Dispose()
    $content.Dispose()
    Write-Output 'PASS logout rejects forged request'
    $r = $client.GetAsync('/admin').GetAwaiter().GetResult()
    $adminHtml = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $r.Dispose()
    $anti = [regex]::Match($adminHtml, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($anti)) { throw 'Admin antiforgery token missing.' }
    $fields = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $fields.Add('username', 'smoke-admin')
    $fields.Add('password', 'smoke-test-password-123')
    $fields.Add('__RequestVerificationToken', $anti)
    $form = [System.Net.Http.FormUrlEncodedContent]::new($fields)
    $r = $client.PostAsync('/admin/login', $form).GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 302) { throw 'Admin login failed.' }
    $r.Dispose(); $form.Dispose()
    $r = $client.GetAsync('/admin/export').GetAwaiter().GetResult()
    $zip = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 200 -or $zip.Length -lt 100 -or $r.Content.Headers.ContentType.MediaType -ne 'application/zip') { throw 'Admin diagnostics export failed.' }
    $r.Dispose()
    Write-Output 'PASS administrator login and diagnostics export'
    $r = $client.GetAsync('/admin').GetAwaiter().GetResult()
    $adminHtml = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult(); $r.Dispose()
    $anti = [regex]::Match($adminHtml, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $fields = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $fields.Add('enabled', 'true'); $fields.Add('message', 'Smoke maintenance'); $fields.Add('__RequestVerificationToken', $anti)
    $form = [System.Net.Http.FormUrlEncodedContent]::new($fields)
    $r = $client.PostAsync('/admin/maintenance', $form).GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 302) { throw 'Enabling maintenance failed.' }
    $r.Dispose(); $form.Dispose()
    $r = $client.GetAsync('/').GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 503 -or !$r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('Smoke maintenance')) { throw 'Maintenance response failed.' }
    $r.Dispose()
    $r = $client.GetAsync('/admin').GetAwaiter().GetResult()
    $adminHtml = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult(); $r.Dispose()
    $anti = [regex]::Match($adminHtml, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $fields = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $fields.Add('enabled', 'false'); $fields.Add('message', 'Smoke maintenance'); $fields.Add('__RequestVerificationToken', $anti)
    $form = [System.Net.Http.FormUrlEncodedContent]::new($fields)
    $r = $client.PostAsync('/admin/maintenance', $form).GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 302) { throw 'Disabling maintenance failed.' }
    $r.Dispose(); $form.Dispose()
    $r = $client.GetAsync('/').GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 200) { throw 'Site did not recover after maintenance.' }
    $r.Dispose()
    Write-Output 'PASS administrator maintenance controls'
    Write-Output 'All HTTP smoke checks passed.'
} finally {
    $client.Dispose()
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
    $expected = [System.IO.Path]::GetFullPath($testData)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($expected.StartsWith($tempRoot) -and [System.IO.Path]::GetFileName($expected).StartsWith('devpulse-smoke-') -and (Test-Path -LiteralPath $expected)) {
        Remove-Item -LiteralPath $expected -Recurse -Force
    }
}
