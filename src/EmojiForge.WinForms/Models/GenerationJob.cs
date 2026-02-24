namespace EmojiForge.WinForms.Models;

public class GenerationJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public string Prompt { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public bool GenerateAll { get; set; }
    public string? Emoji { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
}
