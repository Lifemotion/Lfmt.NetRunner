using Microsoft.AspNetCore.Mvc;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;
using System.IO.Compression;

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
        var status = await _systemd.GetAppStatus(name);
        return Ok(new { status = status.ToString() });
    }

    [HttpGet("{name}/logs")]
    public async Task<IActionResult> GetLogs(string name)
    {
        var logs = await _systemd.GetJournalLogs(name, _settings.Current.JournalLines);
        return Ok(new { logs });
    }

    [HttpPost("{name}/start")]
    public async Task<IActionResult> Start(string name)
    {
        await _systemd.Start(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/stop")]
    public async Task<IActionResult> Stop(string name)
    {
        await _systemd.Stop(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/restart")]
    public async Task<IActionResult> Restart(string name)
    {
        await _systemd.Restart(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/rollback")]
    public async Task<IActionResult> Rollback(string name)
    {
        var ok = await _deploy.Rollback(name);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }

    [HttpPost("{name}/delete")]
    public async Task<IActionResult> Delete(string name)
    {
        if (_appManager.GetAppConfig(name) == null)
            return NotFound(new { error = $"App '{name}' not found" });

        await _appManager.DeleteApp(name);
        return Ok(new { ok = true });
    }

    [HttpPost("{name}/deploy")]
    public async Task<IActionResult> Deploy(string name, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (_appManager.GetAppConfig(name) == null)
            return NotFound(new { error = $"App '{name}' not found. Use POST /api/apps/create to create first." });

        using var stream = file.OpenReadStream();
        var ok = await _deploy.DeployFromArchive(name, stream, file.FileName);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }

    /// <summary>
    /// Create a new app and deploy from archive containing .netrunner file.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        // Extract to temp dir to read .netrunner
        var tempDir = Path.Combine(Path.GetTempPath(), "netrunner-create-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);

            if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = file.OpenReadStream();
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(tempDir);
            }
            else
            {
                var tmpFile = Path.GetTempFileName();
                try
                {
                    await using (var fs = System.IO.File.Create(tmpFile))
                        await file.CopyToAsync(fs);
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "tar", Arguments = $"xzf \"{tmpFile}\" -C \"{tempDir}\"",
                        UseShellExecute = false, RedirectStandardError = true,
                    };
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    await proc.WaitForExitAsync();
                }
                finally { System.IO.File.Delete(tmpFile); }
            }

            // Find .netrunner
            var netrunnerPath = FindNetrunnerFile(tempDir);
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

    private static string? FindNetrunnerFile(string dir)
    {
        var path = Path.Combine(dir, ".netrunner");
        if (System.IO.File.Exists(path)) return path;

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            path = Path.Combine(subDir, ".netrunner");
            if (System.IO.File.Exists(path)) return path;
        }

        return null;
    }
}
