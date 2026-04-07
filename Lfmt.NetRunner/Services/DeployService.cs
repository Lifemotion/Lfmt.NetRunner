using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Services;

public class DeployService
{
    private readonly AppManager _appManager;
    private readonly SystemdService _systemd;
    private readonly HealthCheckService _healthCheck;
    private readonly ServiceFileGenerator _serviceFileGen;
    private readonly NetRunnerConfig _config;
    private readonly ILogger<DeployService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DeployService(
        AppManager appManager,
        SystemdService systemd,
        HealthCheckService healthCheck,
        ServiceFileGenerator serviceFileGen,
        NetRunnerConfig config,
        ILogger<DeployService> logger)
    {
        _appManager = appManager;
        _systemd = systemd;
        _healthCheck = healthCheck;
        _serviceFileGen = serviceFileGen;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> DeployFromArchive(string appName, Stream archiveStream, string fileName)
    {
        var semaphore = _locks.GetOrAdd(appName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            var appConfig = _appManager.GetAppConfig(appName)
                ?? throw new InvalidOperationException($"App '{appName}' not found");

            var appDir = _appManager.GetAppDir(appName);
            var sourceDir = Path.Combine(appDir, "source");

            // Clean up any previous source
            if (Directory.Exists(sourceDir))
                Directory.Delete(sourceDir, true);
            Directory.CreateDirectory(sourceDir);

            // Extract archive
            _logger.LogInformation("Extracting archive for {App}", appName);
            await ExtractArchive(archiveStream, fileName, sourceDir);

            // Check for .netrunner file and update config if present
            var netrunnerFile = FindNetrunnerFile(sourceDir);
            if (netrunnerFile != null)
            {
                var ini = IniParser.Parse(await File.ReadAllTextAsync(netrunnerFile));
                var newConfig = AppConfig.FromIni(ini);
                // Keep the original name
                newConfig.Name = appName;
                await _appManager.UpdateApp(appName, newConfig);
                appConfig = newConfig;
            }

            return await BuildAndDeploy(appName, appConfig, sourceDir, null);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> DeployFromGit(string appName, string cloneUrl, string branch, string? commitId = null)
    {
        var semaphore = _locks.GetOrAdd(appName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            var appConfig = _appManager.GetAppConfig(appName)
                ?? throw new InvalidOperationException($"App '{appName}' not found");

            var appDir = _appManager.GetAppDir(appName);
            var sourceDir = Path.Combine(appDir, "source");

            // Clean up
            if (Directory.Exists(sourceDir))
                Directory.Delete(sourceDir, true);

            // Clone
            _logger.LogInformation("Cloning {Url} branch {Branch} for {App}", cloneUrl, branch, appName);
            var cloneResult = await RunProcess("git",
                $"clone --depth 1 --branch {branch} {cloneUrl} {sourceDir}",
                workingDir: appDir,
                timeoutSeconds: 300);

            if (!cloneResult.Success)
                throw new InvalidOperationException($"git clone failed: {cloneResult.Error}");

            // Read .netrunner from cloned repo
            var netrunnerFile = FindNetrunnerFile(sourceDir);
            if (netrunnerFile != null)
            {
                var ini = IniParser.Parse(await File.ReadAllTextAsync(netrunnerFile));
                var newConfig = AppConfig.FromIni(ini);
                newConfig.Name = appName;
                await _appManager.UpdateApp(appName, newConfig);
                appConfig = newConfig;
            }

            return await BuildAndDeploy(appName, appConfig, sourceDir, commitId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> Rollback(string appName)
    {
        var appDir = _appManager.GetAppDir(appName);
        var v1Dir = Path.Combine(appDir, "releases", "v1");

        if (!Directory.Exists(v1Dir))
            throw new InvalidOperationException("No previous version available for rollback");

        _logger.LogInformation("Rolling back {App} to v1", appName);

        // Atomic symlink swap to v1
        AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v1");

        await _systemd.DaemonReload();
        await _systemd.Restart(appName);

        var appConfig = _appManager.GetAppConfig(appName)!;
        var healthy = await _healthCheck.CheckHealth(
            appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
            appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

        await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Action = "ROLLBACK",
            Result = healthy ? "SUCCESS" : "FAILURE",
            Message = healthy ? "Rolled back to v1" : "Rollback failed, service stopped",
        });

        if (!healthy)
        {
            await _systemd.Stop(appName);
            return false;
        }

        return true;
    }

    private async Task<bool> BuildAndDeploy(string appName, AppConfig appConfig, string sourceDir, string? commitId)
    {
        var appDir = _appManager.GetAppDir(appName);
        var releasesDir = Path.Combine(appDir, "releases");
        var vNewDir = Path.Combine(releasesDir, "v_new");

        // Clean up any previous v_new
        if (Directory.Exists(vNewDir))
            Directory.Delete(vNewDir, true);

        try
        {
            if (appConfig.IsSourceMode)
            {
                // Source mode: build with dotnet publish
                var projectPath = Path.Combine(sourceDir, appConfig.Project);
                if (!File.Exists(projectPath))
                    throw new InvalidOperationException($"Project file not found: {appConfig.Project}");

                _logger.LogInformation("Building {App}: dotnet publish", appName);
                var buildResult = await RunProcess(_config.DotnetPath,
                    $"publish \"{projectPath}\" -c Release -o \"{vNewDir}\"",
                    workingDir: sourceDir,
                    timeoutSeconds: 600);

                if (!buildResult.Success)
                {
                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "DEPLOY",
                        Result = "FAILURE",
                        Message = $"Build failed: {buildResult.Error}",
                        Commit = commitId,
                    });
                    CleanupDir(vNewDir);
                    CleanupDir(sourceDir);
                    return false;
                }
            }
            else
            {
                // Artifact mode: copy published files directly
                _logger.LogInformation("Deploying pre-built artifacts for {App}", appName);
                CopyDirectory(sourceDir, vNewDir);
            }

            // Verify DLL exists
            var dllPath = Path.Combine(vNewDir, appConfig.GetDllName());
            if (!File.Exists(dllPath))
            {
                await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = "DEPLOY",
                    Result = "FAILURE",
                    Message = $"Expected DLL not found: {appConfig.GetDllName()}",
                    Commit = commitId,
                });
                CleanupDir(vNewDir);
                CleanupDir(sourceDir);
                return false;
            }

            // Generate and install service file
            string? customContent = null;
            if (!string.IsNullOrEmpty(appConfig.CustomServiceFile))
            {
                var customPath = Path.Combine(sourceDir, appConfig.CustomServiceFile);
                if (File.Exists(customPath))
                    customContent = await File.ReadAllTextAsync(customPath);
            }

            var serviceContent = _serviceFileGen.Generate(appConfig, customContent);
            await File.WriteAllTextAsync(Path.Combine(appDir, "app.service"), serviceContent);
            await _systemd.InstallServiceFile(appName);

            // Set ownership
            await _systemd.ChownApp(appName);

            // Atomic symlink swap to v_new
            AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v_new");

            await _systemd.DaemonReload();
            await _systemd.Enable(appName);
            await _systemd.Restart(appName);

            // Health check
            var healthy = await _healthCheck.CheckHealth(
                appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
                appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

            if (healthy)
            {
                // Finalize rotation: v1 deleted, v2 → v1, v_new → v2
                var v1Dir = Path.Combine(releasesDir, "v1");
                var v2Dir = Path.Combine(releasesDir, "v2");

                CleanupDir(v1Dir);
                if (Directory.Exists(v2Dir))
                    Directory.Move(v2Dir, v1Dir);
                Directory.Move(vNewDir, v2Dir);

                AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v2");

                await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = "DEPLOY",
                    Result = "SUCCESS",
                    Message = "Built and deployed successfully",
                    Commit = commitId,
                });

                _logger.LogInformation("Deploy of {App} succeeded", appName);
            }
            else
            {
                // Auto-rollback
                _logger.LogWarning("Health check failed for {App}, rolling back", appName);

                var v2Dir = Path.Combine(releasesDir, "v2");
                if (Directory.Exists(v2Dir))
                {
                    AtomicSymlinkSwap(Path.Combine(appDir, "current"), "releases/v2");
                    await _systemd.Restart(appName);

                    var rollbackHealthy = await _healthCheck.CheckHealth(
                        appConfig.Port, appConfig.HealthPath, appConfig.HealthPhrase,
                        appConfig.HealthTimeoutSeconds, appConfig.HealthIntervalSeconds);

                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "ROLLBACK",
                        Result = rollbackHealthy ? "SUCCESS" : "FAILURE",
                        Message = rollbackHealthy
                            ? "Auto-rolled back to v2"
                            : "Rollback also failed, service stopped",
                        Commit = commitId,
                    });

                    if (!rollbackHealthy)
                        await _systemd.Stop(appName);
                }
                else
                {
                    await _systemd.Stop(appName);
                    await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Action = "DEPLOY",
                        Result = "FAILURE",
                        Message = "First deploy failed, no rollback available, service stopped",
                        Commit = commitId,
                    });
                }
            }

            CleanupDir(sourceDir);
            return healthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deploy failed for {App}", appName);
            CleanupDir(vNewDir);
            CleanupDir(sourceDir);

            await _appManager.AppendDeployLog(appName, new DeploymentLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = "DEPLOY",
                Result = "FAILURE",
                Message = $"Deploy error: {ex.Message}",
                Commit = commitId,
            });

            throw;
        }
    }

    private static void AtomicSymlinkSwap(string linkPath, string target)
    {
        var dir = Path.GetDirectoryName(linkPath)!;
        var name = Path.GetFileName(linkPath);

        // ln -sfn creates symlink atomically with rename
        var psi = new ProcessStartInfo
        {
            FileName = "ln",
            Arguments = $"-sfn {target} {name}",
            WorkingDirectory = dir,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
    }

    private async Task<ProcessResult> RunProcess(string fileName, string arguments,
        string? workingDir = null, int timeoutSeconds = 120)
    {
        _logger.LogInformation("Running: {File} {Args}", fileName, arguments);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? "",
        };

        using var process = Process.Start(psi)!;
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return new ProcessResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = error,
                ExitCode = process.ExitCode,
            };
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new ProcessResult
            {
                Success = false,
                Error = $"Process timed out after {timeoutSeconds}s",
                ExitCode = -1,
            };
        }
    }

    private static void CleanupDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private static string? FindNetrunnerFile(string dir)
    {
        var path = Path.Combine(dir, ".netrunner");
        if (File.Exists(path)) return path;

        // Check one level down (in case archive has a root folder)
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            path = Path.Combine(subDir, ".netrunner");
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static async Task ExtractArchive(Stream stream, string fileName, string destDir)
    {
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destDir);
        }
        else if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            // Save to temp file, then extract with tar
            var tmpFile = Path.GetTempFileName();
            try
            {
                await using (var fs = File.Create(tmpFile))
                    await stream.CopyToAsync(fs);

                var psi = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"xzf \"{tmpFile}\" -C \"{destDir}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi)!;
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"tar extract failed: {error}");
                }
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported archive format: {fileName}");
        }
    }

    private record ProcessResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = "";
        public string Error { get; init; } = "";
        public int ExitCode { get; init; }
    }
}
