using System.Text.Json.Serialization;

namespace Lfmt.NetRunner.Models;

public class ForgejoWebhookPayload
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = "";

    [JsonPropertyName("repository")]
    public ForgejoRepository Repository { get; set; } = new();

    [JsonPropertyName("head_commit")]
    public ForgejoCommit? HeadCommit { get; set; }

    public string GetBranch() =>
        Ref.StartsWith("refs/heads/") ? Ref["refs/heads/".Length..] : Ref;
}

public class ForgejoRepository
{
    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; set; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";
}

public class ForgejoCommit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
