using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;
    private UiSettings _settings;

    public SettingsService(NetRunnerConfig config, ILogger<SettingsService> logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(config.AppsRoot, "settings.ini");
        _settings = Load();
    }

    public UiSettings Current => _settings;

    public async Task SaveAsync(UiSettings settings)
    {
        _settings = settings;
        var ini = settings.ToIni();
        await File.WriteAllTextAsync(_settingsPath, IniParser.Serialize(ini));
        _logger.LogInformation("Settings saved to {Path}", _settingsPath);
    }

    private UiSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            _logger.LogInformation("No settings file at {Path}, using defaults", _settingsPath);
            return new UiSettings();
        }

        try
        {
            var content = File.ReadAllText(_settingsPath);
            var ini = IniParser.Parse(content);
            _logger.LogInformation("Loaded settings from {Path}", _settingsPath);
            return UiSettings.FromIni(ini);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}", _settingsPath);
            return new UiSettings();
        }
    }
}
