using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class ServiceFileGenerator
{
    private readonly NetRunnerConfig _runnerConfig;

    private const string Template = """
        [Unit]
        Description={{app_name}} (.NET app managed by NetRunner)
        After=network.target

        [Service]
        WorkingDirectory=/var/lib/netrunner/apps/{{app_name}}/current
        ExecStart={{dotnet_path}} /var/lib/netrunner/apps/{{app_name}}/current/{{dll_name}}
        Restart=always
        RestartSec=5
        SyslogIdentifier=netrunner-{{app_name}}

        User=netrunner-{{app_name}}
        Group=netrunner-{{app_name}}

        Environment=ASPNETCORE_URLS=http://localhost:{{port}}
        Environment=DOTNET_NOLOGO=1
        EnvironmentFile=/var/lib/netrunner/apps/{{app_name}}/env

        ProtectSystem=strict
        ReadWritePaths=/var/lib/netrunner/apps/{{app_name}}
        PrivateTmp=true
        ProtectHome=true
        NoNewPrivileges=true
        ProtectKernelTunables=true
        ProtectKernelModules=true
        ProtectControlGroups=true
        PrivateDevices=true

        MemoryMax={{memory}}
        CPUQuota={{cpu}}

        {{extra_directives}}

        [Install]
        WantedBy=multi-user.target
        """;

    // Directives that are always force-overridden in custom files
    private static readonly string[] ForcedDirectives =
    [
        "User=", "Group=", "ProtectSystem=", "PrivateTmp=", "ProtectHome=",
        "NoNewPrivileges=", "ProtectKernelTunables=", "ProtectKernelModules=",
        "ProtectControlGroups=", "PrivateDevices=", "EnvironmentFile="
    ];

    public ServiceFileGenerator(NetRunnerConfig runnerConfig)
    {
        _runnerConfig = runnerConfig;
    }

    public string Generate(AppConfig appConfig, string? customFileContent = null)
    {
        var content = customFileContent ?? Template;

        // Apply placeholder substitutions
        content = content
            .Replace("{{app_name}}", appConfig.Name)
            .Replace("{{port}}", appConfig.Port.ToString())
            .Replace("{{memory}}", appConfig.Memory)
            .Replace("{{cpu}}", appConfig.Cpu)
            .Replace("{{dotnet_path}}", _runnerConfig.DotnetPath)
            .Replace("{{dll_name}}", appConfig.GetDllName())
            .Replace("{{extra_directives}}", appConfig.ExtraDirectives ?? "");

        // Add environment variables from config
        if (appConfig.EnvironmentVariables.Count > 0)
        {
            var envLines = string.Join("\n",
                appConfig.EnvironmentVariables.Select(kv => $"Environment={kv.Key}={kv.Value}"));

            // Insert before [Install]
            content = content.Replace("[Install]", envLines + "\n\n[Install]");
        }

        // Force-override security directives in custom files
        if (customFileContent != null)
        {
            content = ForceSecurityDirectives(content, appConfig);
        }

        // Clean up empty lines from unused placeholders
        var lines = content.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0 || true);

        return string.Join("\n", lines).Trim() + "\n";
    }

    private string ForceSecurityDirectives(string content, AppConfig appConfig)
    {
        var lines = content.Split('\n').ToList();

        // Remove existing security directives
        lines.RemoveAll(l =>
        {
            var trimmed = l.TrimStart();
            return ForcedDirectives.Any(d => trimmed.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        });

        // Find [Service] section and inject forced directives
        var serviceIdx = lines.FindIndex(l => l.Trim() == "[Service]");
        if (serviceIdx < 0) return string.Join("\n", lines);

        var insertIdx = serviceIdx + 1;
        var forced = new[]
        {
            $"User=netrunner-{appConfig.Name}",
            $"Group=netrunner-{appConfig.Name}",
            $"EnvironmentFile=/var/lib/netrunner/apps/{appConfig.Name}/env",
            "ProtectSystem=strict",
            "PrivateTmp=true",
            "ProtectHome=true",
            "NoNewPrivileges=true",
            "ProtectKernelTunables=true",
            "ProtectKernelModules=true",
            "ProtectControlGroups=true",
            "PrivateDevices=true",
        };

        foreach (var directive in forced.Reverse())
            lines.Insert(insertIdx, directive);

        return string.Join("\n", lines);
    }
}
