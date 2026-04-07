using System.Text.Json;
using System.Text.RegularExpressions;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public partial class AppManager
{
    private readonly NetRunnerConfig _config;
    private readonly SystemdService _systemd;
    private readonly ILogger<AppManager> _logger;
    private readonly Dictionary<string, AppConfig> _apps = new();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,46}[a-z0-9]$")]
    private static partial Regex AppNameRegex();

    public AppManager(NetRunnerConfig config, SystemdService systemd, ILogger<AppManager> logger)
    {
        _config = config;
        _systemd = systemd;
        _logger = logger;
        LoadAllApps();
    }

    public IReadOnlyCollection<string> GetAllAppNames() => _apps.Keys.ToList();

    public AppConfig? GetAppConfig(string name) =>
        _apps.TryGetValue(name, out var config) ? config : null;

    public async Task<AppState> GetAppState(string name)
    {
        var config = GetAppConfig(name);
        if (config == null) return new AppState { Name = name, Status = AppStatus.Unknown };

        var appDir = GetAppDir(name);
        var status = await _systemd.GetAppStatus(name);

        // Read last deploy entry
        DateTimeOffset? lastDeployed = null;
        string? lastCommit = null;
        var logPath = Path.Combine(appDir, "deploy.log");
        if (File.Exists(logPath))
        {
            var lastLine = (await File.ReadAllLinesAsync(logPath))
                .LastOrDefault(l => l.Contains("\"DEPLOY\""));
            if (lastLine != null)
            {
                var entry = JsonSerializer.Deserialize<DeploymentLogEntry>(lastLine);
                if (entry != null)
                {
                    lastDeployed = entry.Timestamp;
                    lastCommit = entry.Commit;
                }
            }
        }

        return new AppState
        {
            Name = name,
            Status = status,
            Port = config.Port,
            HasPreviousVersion = Directory.Exists(Path.Combine(appDir, "releases", "v1")),
            LastDeployedAt = lastDeployed,
            LastDeployCommit = lastCommit,
        };
    }

    public async Task<List<(AppConfig Config, AppState State)>> GetAllApps()
    {
        var result = new List<(AppConfig, AppState)>();
        foreach (var name in _apps.Keys)
        {
            var config = _apps[name];
            var state = await GetAppState(name);
            result.Add((config, state));
        }
        return result;
    }

    public async Task CreateApp(AppConfig config)
    {
        ValidateAppName(config.Name);
        if (_apps.ContainsKey(config.Name))
            throw new InvalidOperationException($"App '{config.Name}' already exists");

        ValidatePort(config.Port, config.Name);

        // Create user, directories, set ownership so our process can write
        await _systemd.CreateUser(config.Name);
        await _systemd.InitApp(config.Name);
        await _systemd.ChownApp(config.Name);

        // Now we can write (group netrunner has rwx)
        var appDir = GetAppDir(config.Name);
        var ini = config.ToIni();
        await File.WriteAllTextAsync(Path.Combine(appDir, "config.ini"), IniParser.Serialize(ini));

        // Create empty env file via sudo
        await _systemd.WriteEnv(config.Name, "");

        _apps[config.Name] = config;
        _logger.LogInformation("Created app {Name}", config.Name);
    }

    public async Task UpdateApp(string name, AppConfig config)
    {
        if (!_apps.ContainsKey(name))
            throw new InvalidOperationException($"App '{name}' not found");

        if (config.Port != _apps[name].Port)
            ValidatePort(config.Port, name);

        config.Name = name;
        var appDir = GetAppDir(name);
        var ini = config.ToIni();
        await File.WriteAllTextAsync(Path.Combine(appDir, "config.ini"), IniParser.Serialize(ini));

        _apps[name] = config;
        _logger.LogInformation("Updated app {Name}", name);
    }

    public async Task DeleteApp(string name)
    {
        if (!_apps.ContainsKey(name))
            throw new InvalidOperationException($"App '{name}' not found");

        try { await _systemd.Stop(name); } catch { /* may not be running */ }
        try { await _systemd.Disable(name); } catch { /* may not be enabled */ }
        try { await _systemd.DeleteUser(name); } catch { /* may not exist */ }

        var appDir = GetAppDir(name);
        if (Directory.Exists(appDir))
            Directory.Delete(appDir, true);

        _apps.Remove(name);
        _logger.LogInformation("Deleted app {Name}", name);
    }

    public async Task AppendDeployLog(string name, DeploymentLogEntry entry)
    {
        var logPath = Path.Combine(GetAppDir(name), "deploy.log");
        var json = JsonSerializer.Serialize(entry);
        await File.AppendAllTextAsync(logPath, json + "\n");
    }

    public async Task<List<DeploymentLogEntry>> GetDeployLog(string name, int maxEntries = 50)
    {
        var logPath = Path.Combine(GetAppDir(name), "deploy.log");
        if (!File.Exists(logPath)) return [];

        var lines = await File.ReadAllLinesAsync(logPath);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<DeploymentLogEntry>(l)!)
            .TakeLast(maxEntries)
            .Reverse()
            .ToList();
    }

    public async Task<List<DeploymentLogEntry>> GetGlobalDeployLog(int maxEntries = 100)
    {
        var allEntries = new List<(string App, DeploymentLogEntry Entry)>();
        foreach (var name in _apps.Keys)
        {
            var entries = await GetDeployLog(name, maxEntries);
            allEntries.AddRange(entries.Select(e => (name, e)));
        }
        return allEntries
            .OrderByDescending(x => x.Entry.Timestamp)
            .Take(maxEntries)
            .Select(x =>
            {
                x.Entry.Action = $"[{x.App}] {x.Entry.Action}";
                return x.Entry;
            })
            .ToList();
    }

    public string GetAppDir(string name) => Path.Combine(_config.AppsRoot, name);

    public static void ValidateAppName(string name)
    {
        if (!AppNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid app name '{name}': must match [a-z0-9][a-z0-9-]{{0,46}}[a-z0-9]");
    }

    private void ValidatePort(int port, string excludeApp)
    {
        if (port < 1024 || port > 65535)
            throw new ArgumentException($"Port {port} out of range (1024-65535)");

        var conflict = _apps.FirstOrDefault(a => a.Value.Port == port && a.Key != excludeApp);
        if (conflict.Key != null)
            throw new ArgumentException($"Port {port} already used by '{conflict.Key}'");
    }

    private void LoadAllApps()
    {
        if (!Directory.Exists(_config.AppsRoot))
        {
            _logger.LogWarning("Apps root {Path} does not exist", _config.AppsRoot);
            return;
        }

        foreach (var dir in Directory.GetDirectories(_config.AppsRoot))
        {
            var configPath = Path.Combine(dir, "config.ini");
            if (!File.Exists(configPath)) continue;

            try
            {
                var content = File.ReadAllText(configPath);
                var ini = IniParser.Parse(content);
                var config = AppConfig.FromIni(ini);
                _apps[config.Name] = config;
                _logger.LogInformation("Loaded app {Name}", config.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load config from {Path}", configPath);
            }
        }
    }
}
