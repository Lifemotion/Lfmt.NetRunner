using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class ForgejoService
{
    private readonly NetRunnerConfig _config;
    private readonly ILogger<ForgejoService> _logger;

    public ForgejoService(NetRunnerConfig config, ILogger<ForgejoService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool VerifySignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(_config.WebhookSecret))
        {
            _logger.LogWarning("Webhook secret not configured, rejecting");
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(_config.WebhookSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var computed = HMACSHA256.HashData(secretBytes, payloadBytes);
        var expected = Convert.ToHexStringLower(computed);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader));
    }

    public ForgejoWebhookPayload? ParsePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ForgejoWebhookPayload>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse webhook payload");
            return null;
        }
    }

    /// <summary>
    /// Looks up the app name from the webhook repo mapping.
    /// Returns null if no mapping exists.
    /// </summary>
    public string? ResolveAppName(string cloneUrl)
    {
        // Try exact match
        if (_config.WebhookRepoMapping.TryGetValue(cloneUrl, out var name))
            return name;

        // Try without trailing .git
        var withoutGit = cloneUrl.TrimEnd('/').TrimSuffix(".git");
        var withGit = cloneUrl.TrimEnd('/') + ".git";

        foreach (var (url, appName) in _config.WebhookRepoMapping)
        {
            var normalizedUrl = url.TrimEnd('/');
            if (normalizedUrl == withoutGit || normalizedUrl == withGit ||
                normalizedUrl.TrimSuffix(".git") == withoutGit)
                return appName;
        }

        return null;
    }

    /// <summary>
    /// Returns the clone URL from config (not from payload) for security.
    /// </summary>
    public string? GetConfigCloneUrl(string appName)
    {
        return _config.WebhookRepoMapping
            .FirstOrDefault(kv => kv.Value == appName)
            .Key;
    }
}

internal static class StringExtensions
{
    public static string TrimSuffix(this string str, string suffix) =>
        str.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? str[..^suffix.Length]
            : str;
}
