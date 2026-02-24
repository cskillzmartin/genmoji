using System.Diagnostics;
using System.Text.Json;
using EmojiForge.WinForms.Models;

namespace EmojiForge.WinForms.Services;

public sealed class PythonBridge : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private CancellationTokenSource? _cts;
    private bool _intentionalStop;
    private bool _dependenciesValidated;

    public event Action<string>? OnProgress;
    public event Action<string>? OnResult;
    public event Action<string>? OnError;
    public event Action<string>? OnEmojiList;
    public event Action<string>? OnReady;
    public event Action<string>? OnCanceled;
    public event Action<string>? OnLog;

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(AppSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        CleanupStoppedProcess();
        _intentionalStop = false;
        var backendScript = ResolveBackendScriptPath(settings.BackendScriptPath);
        if (!File.Exists(backendScript))
        {
            throw new FileNotFoundException(
                $"Python backend script not found. Checked path: {backendScript}",
                backendScript);
        }
        await EnsureBackendDependenciesAsync(settings, backendScript);

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.PythonExecutablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(backendScript) ?? Directory.GetCurrentDirectory()
        };
        startInfo.ArgumentList.Add(backendScript);

        if (!string.IsNullOrWhiteSpace(settings.HuggingFaceToken))
        {
            startInfo.EnvironmentVariables["HF_TOKEN"] = settings.HuggingFaceToken;
        }
        startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            if (!_intentionalStop)
            {
                OnError?.Invoke("Python backend exited unexpectedly.");
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start Python backend process.");
        }

        _stdin = _process.StandardInput;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadStdoutLoop(_process.StandardOutput, _cts.Token));
        _ = Task.Run(() => ReadStderrLoop(_process.StandardError, _cts.Token));

        var resolvedModelPath = ResolveModelPath(settings.ModelPath);
        await SendCommandAsync(new
        {
            cmd = "init",
            model_path = resolvedModelPath,
            device = settings.Device,
            font_path = settings.EmojiFontPath,
            enable_cpu_offload = settings.EnableCpuOffload
        });
    }

    public async Task SendCommandAsync(object command)
    {
        if (_stdin is null)
        {
            throw new InvalidOperationException("Python backend is not running.");
        }

        var line = JsonSerializer.Serialize(command);
        await _stdin.WriteLineAsync(line);
        await _stdin.FlushAsync();
    }

    public async Task StopAsync()
    {
        _intentionalStop = true;

        if (IsRunning)
        {
            try
            {
                await SendCommandAsync(new { cmd = "quit" });
            }
            catch
            {
                // Ignore send failure; process may have exited.
            }

            _cts?.Cancel();

            if (_process is not null && !_process.HasExited)
            {
                if (!_process.WaitForExit(2000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }

        CleanupStoppedProcess();
    }

    private async Task ReadStdoutLoop(StreamReader stdout, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await stdout.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            RouteStdoutLine(line);
        }
    }

    private async Task ReadStderrLoop(StreamReader stderr, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await stderr.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            OnLog?.Invoke(line);
        }
    }

    private void RouteStdoutLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "progress":
                    OnProgress?.Invoke(line);
                    break;
                case "result":
                    OnResult?.Invoke(line);
                    break;
                case "error":
                    OnError?.Invoke(line);
                    break;
                case "emoji_list":
                    OnEmojiList?.Invoke(line);
                    break;
                case "ready":
                    OnReady?.Invoke(line);
                    break;
                case "canceled":
                    OnCanceled?.Invoke(line);
                    break;
                default:
                    OnLog?.Invoke(line);
                    break;
            }
        }
        catch
        {
            OnLog?.Invoke(line);
        }
    }

    public void Dispose()
    {
        _intentionalStop = true;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Best-effort cleanup.
        }

        CleanupStoppedProcess();
    }

    private static string ResolveBackendScriptPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath))
        };

        var fromBase = FindInParents(AppContext.BaseDirectory, Path.Combine("src", "emojiforge_backend", "main.py"));
        if (fromBase is not null)
        {
            candidates.Add(fromBase);
        }

        var fromCwd = FindInParents(Directory.GetCurrentDirectory(), Path.Combine("src", "emojiforge_backend", "main.py"));
        if (fromCwd is not null)
        {
            candidates.Add(fromCwd);
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.FirstOrDefault() ?? configuredPath;
    }

    private static string? FindInParents(string startDir, string relativePath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDir));
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current.FullName, relativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private async Task EnsureBackendDependenciesAsync(AppSettings settings, string backendScript)
    {
        if (_dependenciesValidated)
        {
            return;
        }

        var backendDir = Path.GetDirectoryName(backendScript) ?? Directory.GetCurrentDirectory();
        var checkerScript = Path.Combine(backendDir, "check_dependencies.py");
        if (!File.Exists(checkerScript))
        {
            _dependenciesValidated = true;
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = settings.PythonExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = backendDir
        };
        psi.ArgumentList.Add(checkerScript);
        psi.ArgumentList.Add("--install");
        if (settings.Device.Equals("cuda", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("--require-cuda");
        }

        if (!string.IsNullOrWhiteSpace(settings.HuggingFaceToken))
        {
            psi.Environment["HF_TOKEN"] = settings.HuggingFaceToken;
        }
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        OnLog?.Invoke("Checking backend dependencies...");

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dependency check process.");
        }

        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                stdoutLines.Add(line);
                OnLog?.Invoke(line);
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                stderrLines.Add(line);
                OnLog?.Invoke(line);
            }
        });

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
        {
            var stderr = string.Join(Environment.NewLine, stderrLines);
            var stdout = string.Join(Environment.NewLine, stdoutLines);
            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"Backend dependency check failed (exit {process.ExitCode}). {message}".Trim());
        }

        _dependenciesValidated = true;
        OnLog?.Invoke("Dependencies OK.");
    }

    private void CleanupStoppedProcess()
    {
        try
        {
            _stdin?.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            _process?.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }

        _stdin = null;
        _cts = null;
        _process = null;
    }

    private static readonly string DefaultModelDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EmojiForge",
        "models",
        "FLUX.2-klein-4B");

    /// <summary>
    /// If the configured model path is a HuggingFace repo ID but the model has already
    /// been downloaded to the standard local directory, return the local path instead
    /// to avoid re-downloading.
    /// </summary>
    private static string ResolveModelPath(string configuredModelPath)
    {
        if (Directory.Exists(configuredModelPath))
        {
            return configuredModelPath;
        }

        if (string.Equals(configuredModelPath, "black-forest-labs/FLUX.2-klein-4B", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(DefaultModelDirectory))
        {
            return DefaultModelDirectory;
        }

        return configuredModelPath;
    }
}
