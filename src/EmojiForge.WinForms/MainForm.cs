using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EmojiForge.WinForms.Models;
using EmojiForge.WinForms.Services;

namespace EmojiForge.WinForms;

public partial class MainForm : Form
{
    private const int MaxLogChars = 200_000;
    private const int LogTrimTargetChars = 150_000;

    private readonly SettingsService _settingsService = new();
    private readonly EmojiCatalog _emojiCatalog = new();
    private readonly PythonBridge _bridge = new();
    private readonly ToolTip _previewToolTip = new();

    private AppSettings _settings = new();
    private IReadOnlyList<EmojiInfo> _emojis = [];
    private Stopwatch _runStopwatch = new();
    private string? _activeOutputDir;
    private int _runTotal;
    private int _runCompleted;
    private int _runFailed;
    private long _lastSeedUsed;
    private bool _emojiPickerOpenedForCurrentFocus;
    private bool _diffusionAvailable = true;
    private string _diffusionUnavailableReason = string.Empty;
    private bool _cancelRequested;

    public MainForm()
    {
        InitializeComponent();

        _bridge.OnReady += HandleReady;
        _bridge.OnProgress += HandleProgress;
        _bridge.OnResult += HandleResult;
        _bridge.OnError += HandleError;
        _bridge.OnCanceled += HandleCanceled;
        _bridge.OnLog += HandleLog;

        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        _settings = _settingsService.Load();
        ApplySettingsToControls();
        await RefreshGpuInfoAsync();

        try
        {
            statusLabel.Text = "Starting backend (checking dependencies)...";
            await _bridge.StartAsync(_settings);
            statusLabel.Text = "Loading emoji catalog...";
            _emojis = await _emojiCatalog.LoadFromBackendAsync(_bridge);

            statusLabel.Text = $"Loaded {_emojis.Count} emojis. Ready to generate.";
            AppendLogLine($"Loaded {_emojis.Count} emojis. Ready to generate.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Backend error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            statusLabel.Text = "Failed to initialize backend.";
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        ClearPreviewPanel();
        await _bridge.StopAsync();
        _bridge.Dispose();
        _previewToolTip.Dispose();
    }

    private void ApplySettingsToControls()
    {
        var strength = float.IsFinite(_settings.Strength) &&
            _settings.Strength >= 0.1f &&
            _settings.Strength <= 1.0f
            ? _settings.Strength
            : AppSettings.DefaultStrength;
        var steps = _settings.NumInferenceSteps >= 1 && _settings.NumInferenceSteps <= 50
            ? _settings.NumInferenceSteps
            : AppSettings.DefaultNumInferenceSteps;
        var cfgScale = float.IsFinite(_settings.CfgScale) &&
            _settings.CfgScale >= 1.0f &&
            _settings.CfgScale <= 30.0f
            ? _settings.CfgScale
            : AppSettings.DefaultCfgScale;
        var bgStrength = float.IsFinite(_settings.BackgroundRemovalStrength) &&
            _settings.BackgroundRemovalStrength >= 0.0f &&
            _settings.BackgroundRemovalStrength <= 1.0f
            ? _settings.BackgroundRemovalStrength
            : AppSettings.DefaultBackgroundRemovalStrength;

        promptTextBox.Text = "pixel art style, 16-bit, retro game";
        strengthTrackBar.Value = Math.Clamp((int)Math.Round(strength * 10), 1, 10);
        strengthValueLabel.Text = strength.ToString("0.0");
        stepsUpDown.Value = Math.Clamp(steps, 1, 50);
        cfgScaleUpDown.Value = Math.Clamp((decimal)cfgScale, 1m, 30m);
        seedUpDown.Value = Random.Shared.NextInt64(long.MaxValue);
        randomSeedCheckBox.Checked = _settings.RandomSeed;
        batchSizeUpDown.Value = 1;
        outputSizeComboBox.SelectedItem = _settings.OutputSizePx.ToString();
        removeBgStrengthUpDown.Value = Math.Clamp((decimal)bgStrength, 0m, 1m);
        outputStatusLabel.Text = $"Output: {_settings.DefaultOutputDirectory}";
    }

    private async void GenerateButton_Click(object? sender, EventArgs e)
    {
        generateButton.Enabled = false;
        await StartGenerationAsync();
    }

    private async Task StartGenerationAsync()
    {
        if (string.IsNullOrWhiteSpace(promptTextBox.Text))
        {
            MessageBox.Show(this, "Prompt is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            generateButton.Enabled = true;
            return;
        }

        if (singleEmojiRadio.Checked && GetSelectedEmojis().Count == 0)
        {
            MessageBox.Show(this, "Select one or more emojis in selected mode.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            generateButton.Enabled = true;
            return;
        }

        if (!await EnsureBackendRunningAsync())
        {
            generateButton.Enabled = true;
            return;
        }

        if (!_diffusionAvailable)
        {
            MessageBox.Show(
                this,
                "Diffusion pipeline is not available.\n\n" +
                (_diffusionUnavailableReason.Length > 0 ? _diffusionUnavailableReason : "Check model path and diffusers compatibility."),
                "Backend",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            generateButton.Enabled = true;
            return;
        }

        _runStopwatch = Stopwatch.StartNew();
        _runCompleted = 0;
        _runFailed = 0;
        _cancelRequested = false;

        cancelButton.Enabled = true;
        progressBar.Value = 0;
        ClearPreviewPanel();

        var runFolderName = BuildRunFolderName(promptTextBox.Text);
        _activeOutputDir = Path.Combine(_settings.DefaultOutputDirectory, runFolderName);
        Directory.CreateDirectory(_activeOutputDir);

        outputStatusLabel.Text = $"Output: {_activeOutputDir}";

        var settingsPayload = BuildSettingsPayload();
        var batchSize = CurrentBatchSize;

        if (allEmojisRadio.Checked)
        {
            var emojiCount = _emojis.Count == 0 ? 1 : _emojis.Count;
            _runTotal = Math.Max(1, emojiCount * batchSize);
            progressBar.Maximum = _runTotal;
            await _bridge.SendCommandAsync(new
            {
                cmd = "generate_all",
                prompt = promptTextBox.Text.Trim(),
                output_dir = _activeOutputDir,
                settings = settingsPayload
            });
        }
        else
        {
            var selected = GetSelectedEmojis();
            _runTotal = Math.Max(1, selected.Count * batchSize);
            progressBar.Maximum = Math.Max(1, _runTotal);
            var baseSeed = (long)settingsPayload["seed"];
            var incrementSeed = !sameSeedCheckBox.Checked;
            var generationOrdinal = 0;

            for (var batchIndex = 1; batchIndex <= batchSize; batchIndex++)
            {
                for (var idx = 0; idx < selected.Count; idx++)
                {
                    generationOrdinal++;
                    var emoji = selected[idx];
                    var seed = incrementSeed ? baseSeed + (generationOrdinal - 1) : baseSeed;
                    settingsPayload["seed"] = seed;
                    var outPath = BuildOutputPath(_activeOutputDir!, emoji.Codepoints, seed, batchIndex, batchSize);
                    await _bridge.SendCommandAsync(new
                    {
                        cmd = "generate",
                        job_id = Guid.NewGuid().ToString(),
                        emoji = emoji.Char,
                        prompt = promptTextBox.Text.Trim(),
                        output_path = outPath,
                        settings = settingsPayload
                    });
                }
            }
        }

        WriteRunMetadata(partial: true);
    }

    private Dictionary<string, object> BuildSettingsPayload()
    {
        var seed = randomSeedCheckBox.Checked ? Random.Shared.NextInt64(long.MaxValue) : (long)seedUpDown.Value;
        _lastSeedUsed = seed;
        return new Dictionary<string, object>
        {
            ["strength"] = CurrentStrength,
            ["num_inference_steps"] = (int)stepsUpDown.Value,
            ["guidance_scale"] = CurrentCfgScale,
            ["seed"] = seed,
            ["output_size_px"] = int.Parse(outputSizeComboBox.SelectedItem?.ToString() ?? "512"),
            ["remove_background"] = _settings.RemoveBackground,
            ["remove_background_strength"] = (float)removeBgStrengthUpDown.Value,
            ["same_seed"] = sameSeedCheckBox.Checked,
            ["batch_size"] = CurrentBatchSize
        };
    }

    private async void CancelButton_Click(object? sender, EventArgs e)
    {
        if (_cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        cancelButton.Enabled = false;
        statusLabel.Text = "Cancel requested...";
        etaStatusLabel.Text = "Canceling";
        AppendLogLine("Cancel requested. Waiting for a safe stop point...");

        // Single image generation cannot be interrupted cooperatively mid-step,
        // so fall back to process stop for immediate cancel behavior.
        if (_runTotal <= 1)
        {
            await _bridge.StopAsync();
            _cancelRequested = false;
            generateButton.Enabled = true;
            statusLabel.Text = "Generation canceled.";
            etaStatusLabel.Text = "Canceled";
            WriteRunMetadata(partial: true);
            return;
        }

        if (_bridge.IsRunning)
        {
            try
            {
                await _bridge.SendCommandAsync(new { cmd = "cancel" });
            }
            catch
            {
                _cancelRequested = false;
                generateButton.Enabled = true;
                statusLabel.Text = "Cancel failed (backend unavailable).";
                etaStatusLabel.Text = "Canceled";
                WriteRunMetadata(partial: true);
            }
        }
        else
        {
            _cancelRequested = false;
            generateButton.Enabled = true;
            statusLabel.Text = "Generation canceled.";
            etaStatusLabel.Text = "Canceled";
            WriteRunMetadata(partial: true);
        }
    }

    private void StrengthTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        strengthValueLabel.Text = CurrentStrength.ToString("0.0");
    }

    private void ModeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        emojiInputTextBox.Enabled = singleEmojiRadio.Checked;
    }

    private void OpenEmojiPickerMenuItem_Click(object? sender, EventArgs e)
    {
        if (!singleEmojiRadio.Checked)
        {
            singleEmojiRadio.Checked = true;
        }

        if (!emojiInputTextBox.Focused)
        {
            emojiInputTextBox.Focus();
            return;
        }

        _emojiPickerOpenedForCurrentFocus = true;
        SendWindowsEmojiPickerShortcut();
    }

    private void PasteEmojiMenuItem_Click(object? sender, EventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var text = Clipboard.GetText().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!singleEmojiRadio.Checked)
        {
            singleEmojiRadio.Checked = true;
        }

        emojiInputTextBox.Text = text;
        ApplyEmojiInputText(text);
    }

    private void EmojiInputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            ApplyEmojiInputText(emojiInputTextBox.Text);
        }
    }

    private void EmojiInputTextBox_TextChanged(object? sender, EventArgs e)
    {
        ApplyEmojiInputText(emojiInputTextBox.Text);
    }

    private void EmojiInputTextBox_Enter(object? sender, EventArgs e)
    {
        if (!singleEmojiRadio.Checked || _emojiPickerOpenedForCurrentFocus)
        {
            return;
        }

        _emojiPickerOpenedForCurrentFocus = true;
        SendWindowsEmojiPickerShortcut();
    }

    private void EmojiInputTextBox_Leave(object? sender, EventArgs e)
    {
        _emojiPickerOpenedForCurrentFocus = false;
    }

    private async void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings = dialog.Result;
            _settingsService.Save(_settings);
            ApplySettingsToControls();
            await _bridge.StopAsync();
            await _bridge.StartAsync(_settings);
            await RefreshGpuInfoAsync();
            statusLabel.Text = "Settings applied. Backend restarted.";
        }
    }

    private void OpenOutputButton_Click(object? sender, EventArgs e)
    {
        Directory.CreateDirectory(_settings.DefaultOutputDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.DefaultOutputDirectory}\"")
        {
            UseShellExecute = true
        });
    }

    private void AboutButton_Click(object? sender, EventArgs e)
    {
        MessageBox.Show(this,
            "EmojiForge\nMIT License\nAuthor: cskillzmartin",
            "About",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }



    private void HandleReady(string json)
    {
        BeginInvoke(() =>
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var mode = doc.RootElement.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() : null;
                var fallback = doc.RootElement.TryGetProperty("fallback", out var fallbackProp) && fallbackProp.GetBoolean();
                var message = doc.RootElement.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : string.Empty;
                _diffusionAvailable = !fallback;
                _diffusionUnavailableReason = message ?? string.Empty;

                var readyText = string.IsNullOrWhiteSpace(mode) ? "Backend ready." : $"Backend ready ({mode}).";
                statusLabel.Text = readyText;
                AppendLogLine(readyText);
                generateButton.Enabled = !fallback;
                if (fallback)
                {
                    MessageBox.Show(
                        this,
                        $"Diffusion pipeline is unavailable.\n\n{message}\n\nGeneration will fail until model/pipeline setup is fixed.",
                        "Backend Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch
            {
                _diffusionAvailable = true;
                _diffusionUnavailableReason = string.Empty;
                statusLabel.Text = "Backend ready.";
                AppendLogLine("Backend ready.");
                generateButton.Enabled = true;
            }
        });
    }

    private void HandleProgress(string json)
    {
        BeginInvoke(() =>
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement.GetProperty("current").GetInt32();
            var total = doc.RootElement.GetProperty("total").GetInt32();
            var emoji = doc.RootElement.GetProperty("emoji").GetString() ?? "?";
            progressBar.Maximum = Math.Max(1, total);
            progressBar.Value = Math.Min(current, progressBar.Maximum);
            statusLabel.Text = $"Processing {emoji} ({current}/{total})";

            var elapsed = _runStopwatch.Elapsed;
            if (current > 0 && total > current)
            {
                var avgSeconds = elapsed.TotalSeconds / current;
                var eta = TimeSpan.FromSeconds(avgSeconds * (total - current));
                etaStatusLabel.Text = $"Elapsed: {elapsed:mm\\:ss} | ETA: {eta:mm\\:ss}";
            }
            else
            {
                etaStatusLabel.Text = $"Elapsed: {elapsed:mm\\:ss}";
            }
        });
    }

    private void HandleResult(string json)
    {
        BeginInvoke(() =>
        {
            using var doc = JsonDocument.Parse(json);
            var path = doc.RootElement.GetProperty("output_path").GetString();
            var emoji = doc.RootElement.GetProperty("emoji").GetString() ?? "?";
            var skipped = doc.RootElement.TryGetProperty("skipped", out var skippedProp) && skippedProp.GetBoolean();

            _runCompleted++;
            statusLabel.Text = skipped ? $"Skipped existing {emoji}" : $"Completed {emoji}";
            progressBar.Value = Math.Min(_runCompleted + _runFailed, progressBar.Maximum);

            if (!skipped && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                AddPreview(path, emoji);
            }

            if (_runCompleted + _runFailed >= _runTotal)
            {
                generateButton.Enabled = true;
                cancelButton.Enabled = false;
                _cancelRequested = false;
                etaStatusLabel.Text = $"Completed in {_runStopwatch.Elapsed:mm\\:ss}";
                WriteRunMetadata(partial: false);
            }
            else
            {
                WriteRunMetadata(partial: true);
            }
        });
    }

    private void HandleError(string json)
    {
        BeginInvoke(() =>
        {
            _runFailed++;
            statusLabel.Text = "Error occurred. See log.";
            AppendLogLine(json);
            progressBar.Value = Math.Min(_runCompleted + _runFailed, progressBar.Maximum);
            WriteRunMetadata(partial: _runCompleted + _runFailed < _runTotal);

            if (_runCompleted + _runFailed >= _runTotal)
            {
                generateButton.Enabled = true;
                cancelButton.Enabled = false;
                _cancelRequested = false;
            }
        });
    }

    private void HandleLog(string line)
    {
        BeginInvoke(() =>
        {
            AppendLogLine(line);
        });
    }

    private void HandleCanceled(string json)
    {
        BeginInvoke(() =>
        {
            statusLabel.Text = "Generation canceled.";
            generateButton.Enabled = true;
            cancelButton.Enabled = false;
            _cancelRequested = false;
            etaStatusLabel.Text = "Canceled";
            WriteRunMetadata(partial: true);
        });
    }

    private void AddPreview(string filePath, string emoji)
    {
        using var img = Image.FromFile(filePath);
        var thumb = new Bitmap(img, new Size(72, 72));
        var box = new PictureBox
        {
            Width = 80,
            Height = 80,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = thumb,
            Cursor = Cursors.Hand,
            Tag = new PreviewItemTag { Emoji = emoji, FilePath = filePath }
        };

        _previewToolTip.SetToolTip(box, $"{emoji}\n{filePath}");

        box.DoubleClick += (_, _) => OpenImage(filePath);
        box.ContextMenuStrip = CreatePreviewContextMenu(box);
        previewPanel.Controls.Add(box);
    }

    private void ClearPreviewPanel()
    {
        for (var i = previewPanel.Controls.Count - 1; i >= 0; i--)
        {
            var control = previewPanel.Controls[i];
            if (control is PictureBox box)
            {
                box.Image?.Dispose();
                box.Image = null;
                box.ContextMenuStrip?.Dispose();
                box.ContextMenuStrip = null;
            }

            control.Dispose();
        }

        previewPanel.Controls.Clear();
    }

    private ContextMenuStrip CreatePreviewContextMenu(PictureBox box)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Image", null, (_, _) =>
        {
            if (box.Tag is PreviewItemTag tag)
            {
                OpenImage(tag.FilePath);
            }
        });
        menu.Items.Add("Open Folder", null, (_, _) =>
        {
            if (box.Tag is PreviewItemTag tag)
            {
                OpenFolderSelect(tag.FilePath);
            }
        });
        menu.Items.Add("Copy Path", null, (_, _) =>
        {
            if (box.Tag is PreviewItemTag tag)
            {
                Clipboard.SetText(tag.FilePath);
            }
        });
        menu.Items.Add("Regenerate This Emoji", null, async (_, _) =>
        {
            if (box.Tag is PreviewItemTag tag)
            {
                await RegenerateSingleAsync(tag.Emoji, tag.FilePath);
            }
        });
        return menu;
    }

    private void AppendLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (logTextBox.TextLength > MaxLogChars)
        {
            var removeCount = logTextBox.TextLength - LogTrimTargetChars;
            if (removeCount > 0)
            {
                logTextBox.Select(0, removeCount);
                logTextBox.SelectedText = string.Empty;
            }
        }

        logTextBox.AppendText(line + Environment.NewLine);
        logTextBox.SelectionStart = logTextBox.TextLength;
        logTextBox.ScrollToCaret();
    }

    private async Task RegenerateSingleAsync(string emoji, string filePath)
    {
        if (!generateButton.Enabled)
        {
            MessageBox.Show(this, "Wait until current run completes before regenerating a single emoji.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_bridge.IsRunning)
        {
            MessageBox.Show(this, "Python backend is not running.", "Backend", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        statusLabel.Text = $"Regenerating {emoji}...";
        generateButton.Enabled = false;
        cancelButton.Enabled = true;
        _runTotal = 1;
        _runCompleted = 0;
        _runFailed = 0;
        _runStopwatch = Stopwatch.StartNew();

        await _bridge.SendCommandAsync(new
        {
            cmd = "generate",
            job_id = Guid.NewGuid().ToString(),
            emoji,
            prompt = promptTextBox.Text.Trim(),
            output_path = filePath,
            settings = BuildSettingsPayload()
        });
    }

    private static void OpenImage(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    private static void OpenFolderSelect(string filePath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    private void WriteRunMetadata(bool partial)
    {
        if (string.IsNullOrWhiteSpace(_activeOutputDir))
        {
            return;
        }

        var data = new
        {
            prompt = promptTextBox.Text.Trim(),
            model = _settings.ModelPath,
            date = DateTime.UtcNow.ToString("O"),
            settings = new
            {
                strength = CurrentStrength,
                num_inference_steps = (int)stepsUpDown.Value,
                guidance_scale = CurrentCfgScale,
                seed = _lastSeedUsed,
                output_size_px = int.Parse(outputSizeComboBox.SelectedItem?.ToString() ?? "512"),
                batch_size = CurrentBatchSize
            },
            total_emojis = _runTotal,
            completed = _runCompleted,
            failed = _runFailed,
            partial
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_activeOutputDir, "run_metadata.json"), json);
    }

    private static string BuildRunFolderName(string prompt)
    {
        var cleaned = SanitizePathSegment(prompt);
        cleaned = string.IsNullOrWhiteSpace(cleaned) ? "run" : cleaned;
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{cleaned}_{stamp}";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (invalidChars.Contains(ch))
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_')
            {
                sb.Append(ch);
            }
        }

        var baseText = sb.ToString().Trim().Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return "run";
        }

        const int maxLen = 70;
        if (baseText.Length <= maxLen)
        {
            return baseText.TrimEnd('.', ' ');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseText)))[..8].ToLowerInvariant();
        var prefix = baseText[..55].TrimEnd('_', '.', ' ');
        return $"{prefix}_{hash}";
    }

    private void ApplyEmojiInputText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
    }

    private List<EmojiInfo> GetSelectedEmojis()
    {
        var input = emojiInputTextBox.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var normalizedInput = NormalizeEmojiForMatch(input);
        var matched = new List<(int index, EmojiInfo emoji)>();
        foreach (var emoji in _emojis)
        {
            var normalizedEmoji = NormalizeEmojiForMatch(emoji.Char);
            if (string.IsNullOrEmpty(normalizedEmoji))
            {
                continue;
            }

            var idx = normalizedInput.IndexOf(normalizedEmoji, StringComparison.Ordinal);
            if (idx >= 0)
            {
                matched.Add((idx, emoji));
            }
        }

        var fromCatalog = matched
            .OrderBy(x => x.index)
            .Select(x => x.emoji)
            .DistinctBy(x => x.Char)
            .ToList();
        if (fromCatalog.Count > 0)
        {
            return fromCatalog;
        }

        // Fallback: accept directly entered emoji text elements even if catalog matching misses.
        // This handles variation-selector and platform-specific picker output differences.
        var fallback = new List<EmojiInfo>();
        var elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (!LooksLikeEmojiElement(element))
            {
                continue;
            }

            fallback.Add(new EmojiInfo
            {
                Char = element,
                Name = "Selected Emoji",
                Category = "user-input",
                Codepoints = string.Join("_", element.EnumerateRunes().Select(r => $"{r.Value:X4}"))
            });
        }

        return fallback.DistinctBy(x => x.Char).ToList();
    }

    private static string NormalizeEmojiForMatch(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Normalize common presentation differences so picker/paste variants still match catalog entries.
        return value.Replace("\uFE0F", string.Empty);
    }

    private static bool LooksLikeEmojiElement(string element)
    {
        if (string.IsNullOrWhiteSpace(element))
        {
            return false;
        }

        var hasEmojiSymbol = false;
        foreach (var rune in element.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.OtherSymbol)
            {
                hasEmojiSymbol = true;
            }
        }

        return hasEmojiSymbol;
    }

    private async Task<bool> EnsureBackendRunningAsync()
    {
        if (_bridge.IsRunning)
        {
            return true;
        }

        try
        {
            await _bridge.StartAsync(_settings);
            _emojis = await _emojiCatalog.LoadFromBackendAsync(_bridge);
            statusLabel.Text = "Backend restarted.";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Backend", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task RefreshGpuInfoAsync()
    {
        var gpuInfo = await Task.Run(QueryGpuInfo);
        gpuStatusLabel.Text = gpuInfo;
    }

    private string QueryGpuInfo()
    {
        if (!_settings.Device.Equals("cuda", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU mode";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return "GPU: Unknown";
            }

            var line = proc.StandardOutput.ReadLine();
            proc.WaitForExit(2000);
            if (string.IsNullOrWhiteSpace(line))
            {
                return "GPU: Unknown";
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return $"GPU: {line.Trim()}";
            }

            return $"NVIDIA {parts[0]} | {parts[1]} MB VRAM";
        }
        catch
        {
            return "GPU: Unknown";
        }
    }

    private float CurrentStrength => strengthTrackBar.Value / 10f;
    private float CurrentCfgScale => (float)cfgScaleUpDown.Value;
    private int CurrentBatchSize => (int)batchSizeUpDown.Value;

    private static string BuildOutputPath(string outputDir, string codepoints, long seed, int batchIndex, int batchSize)
    {
        var suffix = batchSize > 1 ? $"_b{batchIndex}" : string.Empty;
        return Path.Combine(outputDir, $"emoji_{codepoints}_s{seed}{suffix}.png");
    }

    private static void SendWindowsEmojiPickerShortcut()
    {
        const byte vkLWin = 0x5B;
        const byte vkPeriod = 0xBE;
        const uint keyUp = 0x0002;

        keybd_event(vkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(vkPeriod, 0, 0, UIntPtr.Zero);
        keybd_event(vkPeriod, 0, keyUp, UIntPtr.Zero);
        keybd_event(vkLWin, 0, keyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private sealed class PreviewItemTag
    {
        public string Emoji { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
    }
}
