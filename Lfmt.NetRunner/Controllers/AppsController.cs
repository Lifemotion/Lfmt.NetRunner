using Microsoft.AspNetCore.Mvc;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Controllers;

[ApiController]
[Route("api/apps")]
public class AppsController : ControllerBase
{
    private readonly SystemdService _systemd;
    private readonly AppManager _appManager;
    private readonly DeployService _deploy;
    private readonly SettingsService _settings;

    public AppsController(SystemdService systemd, AppManager appManager, DeployService deploy, SettingsService settings)
    {
        _systemd = systemd;
        _appManager = appManager;
        _deploy = deploy;
        _settings = settings;
    }

    [HttpGet("{name}/status")]
    public async Task<IActionResult> GetStatus(string name)
    {
        AppManager.ValidateAppName(name);
        var status = await _systemd.GetAppStatus(name);
        return Ok(new { status = status.ToString() });
    }

    [HttpGet("{name}/logs")]
    public async Task<IActionResult> GetLogs(string name)
    {
        AppManager.ValidateAppName(name);
        var logs = await _systemd.GetJournalLogs(name, _settings.Current.JournalLines);
        return Ok(new { logs });
    }

    [HttpPost("{name}/start")]
    public async Task<IActionResult> Start(string name)
    {
        AppManager.ValidateAppName(name);
        await _systemd.Start(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/stop")]
    public async Task<IActionResult> Stop(string name)
    {
        AppManager.ValidateAppName(name);
        await _systemd.Stop(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/restart")]
    public async Task<IActionResult> Restart(string name)
    {
        AppManager.ValidateAppName(name);
        await _systemd.Restart(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/rollback")]
    public async Task<IActionResult> Rollback(string name)
    {
        AppManager.ValidateAppName(name);
        var ok = await _deploy.Rollback(name);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }

    [HttpPost("{name}/delete")]
    public async Task<IActionResult> Delete(string name)
    {
        AppManager.ValidateAppName(name);
        if (_appManager.GetAppConfig(name) == null)
            return NotFound(new { error = $"App '{name}' not found" });

        await _appManager.DeleteApp(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/deploy")]
    public async Task<IActionResult> Deploy(string name, IFormFile file)
    {
        AppManager.ValidateAppName(name);
        ValidateUpload(file);

        if (_appManager.GetAppConfig(name) == null)
            return NotFound(new { error = $"App '{name}' not found. Use POST /api/apps/create to create first." });

        using var stream = file.OpenReadStream();
        var ok = await _deploy.DeployFromArchive(name, stream, file.FileName);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(IFormFile file)
    {
        ValidateUpload(file);

        var tempDir = Path.Combine(Path.GetTempPath(), "netrunner-create-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            await ArchiveHelper.ExtractAsync(file, tempDir);

            var netrunnerPath = ArchiveHelper.FindNetrunnerFile(tempDir);
            if (netrunnerPath == null)
                return BadRequest(new { error = "Archive does not contain a .netrunner file" });

            var ini = IniParser.Parse(await System.IO.File.ReadAllTextAsync(netrunnerPath));
            var config = AppConfig.FromIni(ini);

            if (string.IsNullOrEmpty(config.Name))
                return BadRequest(new { error = ".netrunner file is missing app name" });

            await _appManager.CreateApp(config);

            using var deployStream = file.OpenReadStream();
            var ok = await _deploy.DeployFromArchive(config.Name, deployStream, file.FileName);

            return ok
                ? Created($"/api/apps/{config.Name}/status", new { ok, name = config.Name })
                : StatusCode(500, new { ok, name = config.Name });
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private void ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("No file uploaded");

        var maxBytes = _settings.Current.MaxUploadMb * 1024 * 1024;
        if (file.Length > maxBytes)
            throw new ArgumentException($"File too large ({file.Length / 1024 / 1024} MB). Max: {_settings.Current.MaxUploadMb} MB");
    }
}
