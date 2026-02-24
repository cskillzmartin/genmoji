using System.Diagnostics;
using System.Text.RegularExpressions;
using EmojiForge.WinForms.Models;

namespace EmojiForge.WinForms;

public partial class SettingsForm : Form
{
    private const string DefaultModelRepo = "black-forest-labs/FLUX.2-klein-4B";
    private static readonly Regex PercentRegex = new(@"(?<pct>\d{1,3})%", RegexOptions.Compiled);
    private static readonly string DefaultModelDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EmojiForge",
        "models",
        "FLUX.2-klein-4B");

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current)
    {
        Result = Clone(current);
        InitializeComponent();
        LoadValues(Result);
    }

    private void LoadValues(AppSettings settings)
    {
        var strength = float.IsFinite(settings.Strength) &&
            settings.Strength >= 0.1f &&
            settings.Strength <= 1.0f
            ? settings.Strength
            : AppSettings.DefaultStrength;
        var steps = settings.NumInferenceSteps >= 1 && settings.NumInferenceSteps <= 50
            ? settings.NumInferenceSteps
            : AppSettings.DefaultNumInferenceSteps;
        var cfgScale = float.IsFinite(settings.CfgScale) &&
            settings.CfgScale >= 1.0f &&
            settings.CfgScale <= 30.0f
            ? settings.CfgScale
            : AppSettings.DefaultCfgScale;
        var bgStrength = float.IsFinite(settings.BackgroundRemovalStrength) &&
            settings.BackgroundRemovalStrength >= 0.0f &&
            settings.BackgroundRemovalStrength <= 1.0f
            ? settings.BackgroundRemovalStrength
            : AppSettings.DefaultBackgroundRemovalStrength;

        pythonPathBox.Text = settings.PythonExecutablePath;
        backendPathBox.Text = settings.BackendScriptPath;
        outputDirBox.Text = settings.DefaultOutputDirectory;
        fontPathBox.Text = settings.EmojiFontPath;

        modelPathBox.Text = ResolveModelPathForDisplay(settings.ModelPath);
        hfTokenBox.Text = settings.HuggingFaceToken;
        deviceCombo.SelectedItem = settings.Device;
        cpuOffloadCheck.Checked = settings.EnableCpuOffload;

        strengthUpDown.Value = Math.Clamp((decimal)strength, 0.1m, 1m);
        stepsUpDown.Value = Math.Clamp(steps, 1, 50);
        guidanceUpDown.Value = Math.Clamp((decimal)cfgScale, 1m, 30m);
        seedUpDown.Value = Math.Clamp((decimal)settings.Seed, 0, (decimal)long.MaxValue);
        outputSizeCombo.SelectedItem = settings.OutputSizePx.ToString();
        randomSeedCheck.Checked = settings.RandomSeed;
        removeBackgroundCheck.Checked = settings.RemoveBackground;
        removeBgStrengthUpDown.Value = Math.Clamp((decimal)bgStrength, 0m, 1m);
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        Result = new AppSettings
        {
            PythonExecutablePath = pythonPathBox.Text.Trim(),
            BackendScriptPath = backendPathBox.Text.Trim(),
            DefaultOutputDirectory = outputDirBox.Text.Trim(),
            EmojiFontPath = fontPathBox.Text.Trim(),
            ModelPath = modelPathBox.Text.Trim(),
            HuggingFaceToken = hfTokenBox.Text,
            Device = deviceCombo.SelectedItem?.ToString() ?? "cuda",
            EnableCpuOffload = cpuOffloadCheck.Checked,
            Strength = (float)strengthUpDown.Value,
            NumInferenceSteps = (int)stepsUpDown.Value,
            CfgScale = (float)guidanceUpDown.Value,
            Seed = (long)seedUpDown.Value,
            RandomSeed = randomSeedCheck.Checked,
            OutputSizePx = int.TryParse(outputSizeCombo.SelectedItem?.ToString(), out var size) ? size : 512,
            RemoveBackground = removeBackgroundCheck.Checked,
            BackgroundRemovalStrength = (float)removeBgStrengthUpDown.Value
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        LoadValues(new AppSettings());
    }

    private void BrowseModelPathButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select local folder that contains the downloaded model.",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(modelPathBox.Text) && Directory.Exists(modelPathBox.Text))
        {
            dialog.SelectedPath = modelPathBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            modelPathBox.Text = dialog.SelectedPath;
            modelDownloadStatusLabel.Text = "Model status: local path selected";
        }
    }

    private async void DownloadModelButton_Click(object? sender, EventArgs e)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        var pythonPath = string.IsNullOrWhiteSpace(pythonPathBox.Text) ? "python" : pythonPathBox.Text.Trim();
        var repoId = ResolveRepoIdForDownload(modelPathBox.Text.Trim());
        var defaultRoot = Path.GetDirectoryName(DefaultModelDirectory)!;
        Directory.CreateDirectory(defaultRoot);
        var localDir = DefaultModelDirectory;
        Directory.CreateDirectory(localDir);

        var script = "import sys; from huggingface_hub import snapshot_download; snapshot_download(repo_id=sys.argv[1], local_dir=sys.argv[2], local_dir_use_symlinks=False, resume_download=True); print(sys.argv[2])";

        downloadModelButton.Enabled = false;
        browseModelPathButton.Enabled = false;
        modelDownloadStatusLabel.Text = "Model status: preparing...";
        modelDownloadProgressBar.Value = 0;
        modelDownloadLogBox.Clear();
        SetActiveStage(DownloadStage.Prepare);

        try
        {
            var token = hfTokenBox.Text;
            AppendDownloadLog($"Using Python: {pythonPath}");
            AppendDownloadLog($"Target model: {repoId}");
            AppendDownloadLog($"Download path: {localDir}");

            await EnsureHuggingFaceHubInstalledAsync(pythonPath);

            modelDownloadStatusLabel.Text = "Model status: downloading...";
            SetActiveStage(DownloadStage.Download);
            var environment = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(token))
            {
                environment["HF_TOKEN"] = token;
            }

            var download = await RunProcessWithStreamingAsync(
                pythonPath,
                ["-c", script, repoId, localDir],
                onStdoutLine: HandleDownloadOutputLine,
                onStderrLine: HandleDownloadOutputLine,
                environment: environment);
            if (download.ExitCode != 0)
            {
                var details = !string.IsNullOrWhiteSpace(download.Stderr) ? download.Stderr : download.Stdout;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? "Model download failed." : details.Trim());
            }

            if (!CanUpdateUi())
            {
                return;
            }

            SetActiveStage(DownloadStage.Verify);
            await Task.Delay(250);
            if (!CanUpdateUi())
            {
                return;
            }

            modelPathBox.Text = localDir;
            modelDownloadProgressBar.Value = 100;
            SetActiveStage(DownloadStage.Complete);
            modelDownloadStatusLabel.Text = "Model status: download complete";
            ShowInfo($"Model downloaded to:\n{localDir}", "Download complete");
        }
        catch (Exception ex)
        {
            if (!CanUpdateUi())
            {
                return;
            }

            modelDownloadStatusLabel.Text = "Model status: failed";
            ShowError(
                $"Model download failed.\n\n{ex.Message}\n\nMake sure Python and huggingface_hub are installed.",
                "Download failed");
        }
        finally
        {
            if (CanUpdateUi())
            {
                downloadModelButton.Enabled = true;
                browseModelPathButton.Enabled = true;
            }
        }
    }

    private async Task EnsureHuggingFaceHubInstalledAsync(string pythonPath)
    {
        SetActiveStage(DownloadStage.Bootstrap);
        var check = await RunProcessAsync(pythonPath, ["-c", "import huggingface_hub"]);
        if (check.ExitCode == 0)
        {
            AppendDownloadLog("huggingface_hub already installed.");
            return;
        }

        modelDownloadStatusLabel.Text = "Model status: installing huggingface_hub...";
        AppendDownloadLog("Installing missing dependency: huggingface_hub");

        _ = await RunProcessWithStreamingAsync(
            pythonPath,
            ["-m", "ensurepip", "--upgrade"],
            HandleDownloadOutputLine,
            HandleDownloadOutputLine);
        _ = await RunProcessWithStreamingAsync(
            pythonPath,
            ["-m", "pip", "install", "--upgrade", "pip"],
            HandleDownloadOutputLine,
            HandleDownloadOutputLine);
        var install = await RunProcessWithStreamingAsync(
            pythonPath,
            ["-m", "pip", "install", "huggingface_hub"],
            HandleDownloadOutputLine,
            HandleDownloadOutputLine);
        if (install.ExitCode != 0)
        {
            var details = !string.IsNullOrWhiteSpace(install.Stderr) ? install.Stderr : install.Stdout;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? "Failed to install huggingface_hub in the configured Python environment."
                : details.Trim());
        }
    }

    private static async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start process: {fileName}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = await stdoutTask,
            Stderr = await stderrTask
        };
    }

    private static async Task<ProcessRunResult> RunProcessWithStreamingAsync(
        string fileName,
        IReadOnlyList<string> args,
        Action<string>? onStdoutLine,
        Action<string>? onStderrLine,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start process: {fileName}");
        }

        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                stdoutLines.Add(line);
                onStdoutLine?.Invoke(line);
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                stderrLines.Add(line);
                onStderrLine?.Invoke(line);
            }
        });

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        return new ProcessRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = string.Join(Environment.NewLine, stdoutLines),
            Stderr = string.Join(Environment.NewLine, stderrLines)
        };
    }

    private void HandleDownloadOutputLine(string line)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => HandleDownloadOutputLine(line));
            return;
        }

        AppendDownloadLog(line);
        var match = PercentRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups["pct"].Value, out var pct))
        {
            modelDownloadProgressBar.Value = Math.Clamp(pct, 0, 100);
            modelDownloadStatusLabel.Text = $"Model status: downloading... {pct}%";
        }
    }

    private void AppendDownloadLog(string line)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendDownloadLog(line));
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        modelDownloadLogBox.AppendText(line + Environment.NewLine);
        modelDownloadLogBox.SelectionStart = modelDownloadLogBox.TextLength;
        modelDownloadLogBox.ScrollToCaret();
    }

    private void SetActiveStage(DownloadStage stage)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => SetActiveStage(stage));
            return;
        }

        SetStageVisual(stagePrepareLabel, stage >= DownloadStage.Prepare);
        SetStageVisual(stageBootstrapLabel, stage >= DownloadStage.Bootstrap);
        SetStageVisual(stageDownloadLabel, stage >= DownloadStage.Download);
        SetStageVisual(stageVerifyLabel, stage >= DownloadStage.Verify);
        SetStageVisual(stageCompleteLabel, stage >= DownloadStage.Complete);
    }

    private static void SetStageVisual(Label label, bool active)
    {
        label.BackColor = active ? Color.FromArgb(51, 153, 255) : Color.Gainsboro;
        label.ForeColor = active ? Color.White : Color.Black;
    }

    private bool CanUpdateUi() => !IsDisposed && !Disposing;

    private void ShowInfo(string text, string caption)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        MessageBox.Show(this, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowError(string text, string caption)
    {
        if (!CanUpdateUi())
        {
            return;
        }

        MessageBox.Show(this, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string ResolveRepoIdForDownload(string modelPathOrId)
    {
        if (string.IsNullOrWhiteSpace(modelPathOrId))
        {
            return DefaultModelRepo;
        }

        if (Directory.Exists(modelPathOrId))
        {
            return DefaultModelRepo;
        }

        return modelPathOrId;
    }

    private static string ResolveModelPathForDisplay(string configuredModelPath)
    {
        if (Directory.Exists(configuredModelPath))
        {
            return configuredModelPath;
        }

        if (string.Equals(configuredModelPath, DefaultModelRepo, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(DefaultModelDirectory))
        {
            return DefaultModelDirectory;
        }

        return configuredModelPath;
    }

    private static AppSettings Clone(AppSettings src)
    {
        return new AppSettings
        {
            PythonExecutablePath = src.PythonExecutablePath,
            BackendScriptPath = src.BackendScriptPath,
            ModelPath = src.ModelPath,
            HuggingFaceToken = src.HuggingFaceToken,
            DefaultOutputDirectory = src.DefaultOutputDirectory,
            EmojiFontPath = src.EmojiFontPath,
            Device = src.Device,
            EnableCpuOffload = src.EnableCpuOffload,
            Strength = src.Strength,
            NumInferenceSteps = src.NumInferenceSteps,
            CfgScale = src.CfgScale,
            Seed = src.Seed,
            RandomSeed = src.RandomSeed,
            OutputSizePx = src.OutputSizePx,
            RemoveBackground = src.RemoveBackground,
            BackgroundRemovalStrength = src.BackgroundRemovalStrength
        };
    }

    private sealed class ProcessRunResult
    {
        public int ExitCode { get; init; }
        public string Stdout { get; init; } = string.Empty;
        public string Stderr { get; init; } = string.Empty;
    }

    private enum DownloadStage
    {
        Prepare = 1,
        Bootstrap = 2,
        Download = 3,
        Verify = 4,
        Complete = 5
    }
}
