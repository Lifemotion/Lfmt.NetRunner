namespace Lfmt.NetRunner.Models;

public class AppState
{
    public string Name { get; set; } = "";
    public AppStatus Status { get; set; } = AppStatus.Unknown;
    public bool HasPreviousVersion { get; set; }
    public DateTimeOffset? LastDeployedAt { get; set; }
    public string? LastDeployCommit { get; set; }
    public string? LastDeployResult { get; set; }
    public string CurrentVersion { get; set; } = "";
    public int Port { get; set; }
}

public enum AppStatus
{
    Running,
    Stopped,
    Failed,
    Deploying,
    Unknown
}
