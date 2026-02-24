using System.Text.Json;
using EmojiForge.WinForms.Models;

namespace EmojiForge.WinForms.Services;

public sealed class EmojiCatalog
{
    public async Task<IReadOnlyList<EmojiInfo>> LoadFromBackendAsync(PythonBridge bridge)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<EmojiInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnList(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var list = new List<EmojiInfo>();
                foreach (var item in doc.RootElement.GetProperty("emojis").EnumerateArray())
                {
                    list.Add(new EmojiInfo
                    {
                        Char = ReadAsString(item.GetProperty("char")),
                        Name = ReadAsString(item.GetProperty("name"), "Unknown"),
                        Category = item.TryGetProperty("category", out var category) ? ReadAsString(category) : string.Empty,
                        Codepoints = item.TryGetProperty("codepoints", out var codepoints) ? ReadAsString(codepoints) : string.Empty
                    });
                }

                tcs.TrySetResult(list.OrderBy(x => x.Name).ToList());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        bridge.OnEmojiList += OnList;
        await bridge.SendCommandAsync(new { cmd = "list_emojis" });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var registration = timeout.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        finally
        {
            bridge.OnEmojiList -= OnList;
        }
    }

    private static string ReadAsString(JsonElement element, string fallback = "")
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => fallback,
            JsonValueKind.Undefined => fallback,
            _ => element.ToString()
        };
    }
}
