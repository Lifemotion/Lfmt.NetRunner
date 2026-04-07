using System.Text.Json.Serialization;

namespace Lfmt.NetRunner.Models;

public class DeploymentLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }
}
