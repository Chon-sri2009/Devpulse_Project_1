namespace MiniProject_Everything_1.Services;

public sealed class OperationsSettings
{
    public string[] ApprovedHosts { get; set; } = [];
    public int[] ApprovedPorts { get; set; } = [];
    public string[] ApprovedUrls { get; set; } = [];
    public string[] ApprovedLogFiles { get; set; } = [];
    public DatabaseTarget[] Databases { get; set; } = [];
    public int LoadTestMaxRequests { get; set; } = 25;
    public int LoadTestMaxConcurrency { get; set; } = 5;
    public int WatchIntervalSeconds { get; set; } = 300;
}

public sealed class DatabaseTarget
{
    public string Name { get; set; } = "Database";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string Kind { get; set; } = "TCP";
}

public sealed class AlertSettings
{
    public double MemoryMb { get; set; } = 1024;
    public double DiskUsedPercent { get; set; } = 90;
    public int RequestLatencyMs { get; set; } = 2000;
    public string? WebhookUrl { get; set; }
    public SmtpAlertSettings Smtp { get; set; } = new();
}

public sealed class SmtpAlertSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public bool EnableSsl { get; set; } = true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To);
}

public sealed class AdminSettings
{
    public string Username { get; set; } = "admin";
    public string? Password { get; set; }
    public bool AllowProcessTermination { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Password) && Password.Length >= 12;
}
