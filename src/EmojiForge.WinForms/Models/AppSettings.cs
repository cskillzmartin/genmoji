using System.Text.Json.Serialization;

namespace EmojiForge.WinForms.Models;

public class AppSettings
{
    public const float DefaultStrength = 0.1f;
    public const int DefaultNumInferenceSteps = 30;
    public const float DefaultCfgScale = 1.0f;
    public const bool DefaultRemoveBackground = true;
    public const float DefaultBackgroundRemovalStrength = 1.0f;

    public string PythonExecutablePath { get; set; } = "python";
    public string BackendScriptPath { get; set; } = @".\src\emojiforge_backend\main.py";
    public string ModelPath { get; set; } = "black-forest-labs/FLUX.2-klein-4B";
    public string HuggingFaceToken { get; set; } = string.Empty;
    public string DefaultOutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "EmojiForge");
    public string EmojiFontPath { get; set; } = @"C:\Windows\Fonts\seguiemj.ttf";
    public string Device { get; set; } = "cuda";
    public bool EnableCpuOffload { get; set; }

    public float Strength { get; set; } = DefaultStrength;
    public int NumInferenceSteps { get; set; } = DefaultNumInferenceSteps;
    [JsonPropertyName("cfgScale")]
    public float CfgScale { get; set; } = DefaultCfgScale;
    // Backward compatibility with older settings files using guidanceScale.
    [JsonPropertyName("guidanceScale")]
    public float GuidanceScale
    {
        get => CfgScale;
        set => CfgScale = value;
    }

    public long Seed { get; set; } = 42;
    public bool RandomSeed { get; set; } = true;
    public int OutputSizePx { get; set; } = 512;
    public bool RemoveBackground { get; set; } = DefaultRemoveBackground;
    public float BackgroundRemovalStrength { get; set; } = DefaultBackgroundRemovalStrength;

    [JsonIgnore]
    public string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EmojiForge");
}
