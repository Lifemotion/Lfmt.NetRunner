namespace Lfmt.NetRunner.Models;

public class AppConfig
{
    // [app]
    public string Name { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string Project { get; set; } = "";
    public string? Dll { get; set; }

    public bool IsSourceMode => !string.IsNullOrEmpty(Project);

    // [health]
    public string HealthPath { get; set; } = "/health";
    public string HealthPhrase { get; set; } = "Healthy";
    public int HealthTimeoutSeconds { get; set; } = 30;
    public int HealthIntervalSeconds { get; set; } = 3;

    // [resources]
    public string Memory { get; set; } = "256M";
    public string Cpu { get; set; } = "100%";

    // [env]
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    // [service]
    public string? CustomServiceFile { get; set; }
    public string? ExtraDirectives { get; set; }

    public static AppConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var config = new AppConfig();

        if (ini.TryGetValue("app", out var app))
        {
            if (app.TryGetValue("name", out var name)) config.Name = name;
            if (app.TryGetValue("port", out var port) && int.TryParse(port, out var p)) config.Port = p;
            if (app.TryGetValue("project", out var project)) config.Project = project;
            if (app.TryGetValue("dll", out var dll)) config.Dll = dll;
        }

        if (ini.TryGetValue("health", out var health))
        {
            if (health.TryGetValue("path", out var path)) config.HealthPath = path;
            if (health.TryGetValue("phrase", out var phrase)) config.HealthPhrase = phrase;
            if (health.TryGetValue("timeout", out var timeout) && int.TryParse(timeout, out var t)) config.HealthTimeoutSeconds = t;
            if (health.TryGetValue("interval", out var interval) && int.TryParse(interval, out var i)) config.HealthIntervalSeconds = i;
        }

        if (ini.TryGetValue("resources", out var resources))
        {
            if (resources.TryGetValue("memory", out var memory)) config.Memory = memory;
            if (resources.TryGetValue("cpu", out var cpu)) config.Cpu = cpu;
        }

        if (ini.TryGetValue("env", out var env))
        {
            config.EnvironmentVariables = new Dictionary<string, string>(env);
        }

        if (ini.TryGetValue("service", out var service))
        {
            if (service.TryGetValue("custom_file", out var customFile)) config.CustomServiceFile = customFile;
            if (service.TryGetValue("extra_directives", out var extra)) config.ExtraDirectives = extra;
        }

        return config;
    }

    public Dictionary<string, Dictionary<string, string>> ToIni()
    {
        var ini = new Dictionary<string, Dictionary<string, string>>
        {
            ["app"] = new()
            {
                ["name"] = Name,
                ["port"] = Port.ToString(),
            },
        };

        if (!string.IsNullOrEmpty(Project)) ini["app"]["project"] = Project;
        if (!string.IsNullOrEmpty(Dll)) ini["app"]["dll"] = Dll;

        ini["health"] = new()
        {
            ["path"] = HealthPath,
            ["phrase"] = HealthPhrase,
            ["timeout"] = HealthTimeoutSeconds.ToString(),
            ["interval"] = HealthIntervalSeconds.ToString()
        };
        ini["resources"] = new()
        {
            ["memory"] = Memory,
            ["cpu"] = Cpu
        };

        if (EnvironmentVariables.Count > 0)
            ini["env"] = new Dictionary<string, string>(EnvironmentVariables);

        var svc = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(CustomServiceFile)) svc["custom_file"] = CustomServiceFile;
        if (!string.IsNullOrEmpty(ExtraDirectives)) svc["extra_directives"] = ExtraDirectives;
        if (svc.Count > 0) ini["service"] = svc;

        return ini;
    }

    public string GetDllName()
    {
        if (!string.IsNullOrEmpty(Dll))
            return Dll;
        if (!string.IsNullOrEmpty(Project))
            return Path.GetFileNameWithoutExtension(Project) + ".dll";
        return Name + ".dll";
    }
}
