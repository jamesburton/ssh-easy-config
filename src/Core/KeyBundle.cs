using System.Text;
using System.Text.Json;

namespace SshEasyConfig.Core;

public record KeyBundle(
    string PublicKey,
    string Hostname,
    string Username,
    string? SuggestedAlias)
{
    public const string ClipboardHeader = "--- BEGIN SSH-EASY-CONFIG ---";
    public const string ClipboardFooter = "--- END SSH-EASY-CONFIG ---";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static KeyBundle FromJson(string json) =>
        JsonSerializer.Deserialize<KeyBundle>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize KeyBundle from JSON.");

    public string ToClipboardText()
    {
        var json = ToJson();
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var wrapped = WrapLines(base64, 76);
        return $"{ClipboardHeader}\n{wrapped}\n{ClipboardFooter}";
    }

    public static KeyBundle FromClipboardText(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries);
        var dataLines = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            if (line == ClipboardHeader) { inside = true; continue; }
            if (line == ClipboardFooter) break;
            if (inside && !string.IsNullOrWhiteSpace(line))
                dataLines.Add(line);
        }

        var base64 = string.Join("", dataLines);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return FromJson(json);
    }

    public void SaveToFile(string path) => File.WriteAllText(path, ToJson());

    public static KeyBundle LoadFromFile(string path) => FromJson(File.ReadAllText(path));

    private static string WrapLines(string input, int lineLength)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i += lineLength)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(input.AsSpan(i, Math.Min(lineLength, input.Length - i)));
        }
        return sb.ToString();
    }
}
