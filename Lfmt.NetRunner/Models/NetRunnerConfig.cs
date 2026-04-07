namespace Lfmt.NetRunner.Models;

public class NetRunnerConfig
{
    public string Listen { get; set; } = "127.0.0.1:5050";
    public string WebhookSecret { get; set; } = "";
    public string AppsRoot { get; set; } = "/var/lib/netrunner/apps";
    public string DotnetPath { get; set; } = "/usr/bin/dotnet";
    public string SudoScript { get; set; } = "/opt/netrunner/netrunner-sudo.sh";
    public Dictionary<string, string> WebhookRepoMapping { get; set; } = new();

    public static NetRunnerConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var config = new NetRunnerConfig();

        if (ini.TryGetValue("server", out var server))
        {
            if (server.TryGetValue("listen", out var listen)) config.Listen = listen;
        }

        if (ini.TryGetValue("webhook", out var webhook))
        {
            if (webhook.TryGetValue("secret", out var secret)) config.WebhookSecret = secret;
        }

        if (ini.TryGetValue("paths", out var paths))
        {
            if (paths.TryGetValue("apps_root", out var appsRoot)) config.AppsRoot = appsRoot;
            if (paths.TryGetValue("dotnet_path", out var dotnetPath)) config.DotnetPath = dotnetPath;
            if (paths.TryGetValue("sudo_script", out var sudoScript)) config.SudoScript = sudoScript;
        }

        if (ini.TryGetValue("webhooks", out var webhooks))
        {
            config.WebhookRepoMapping = new Dictionary<string, string>(webhooks);
        }

        return config;
    }
}
