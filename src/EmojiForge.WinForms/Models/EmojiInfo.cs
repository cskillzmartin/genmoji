namespace EmojiForge.WinForms.Models;

public class EmojiInfo
{
    public string Char { get; set; } = string.Empty;
    public string Name { get; set; } = "Unknown";
    public string Category { get; set; } = string.Empty;
    public string Codepoints { get; set; } = string.Empty;

    public override string ToString() => $"{Char} {Name}";
}
