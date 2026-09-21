using System.Globalization;
using System.Text.RegularExpressions;

namespace ApiScout.Services;

/// <summary>
/// Request variables: "{city}" in a test URL, header or body stands for the value saved under that name for the API.
/// {key} is not one of them - that is the saved API key and has its own rules. A few names are always there:
/// {today}, {yesterday}, {tomorrow} (yyyy-MM-dd), {now} (UTC, ISO 8601) and {timestamp} (Unix seconds).
/// </summary>
public static partial class RequestVariables
{
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_-]{0,39})\}")]
    private static partial Regex PlaceholderRx();

    public static readonly string[] BuiltIn = ["today", "yesterday", "tomorrow", "now", "timestamp"];

    private static string? BuiltInValue(string name, DateTime now) => name.ToLowerInvariant() switch
    {
        "today" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "yesterday" => now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "tomorrow" => now.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "now" => now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        "timestamp" => new DateTimeOffset(now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>"name = value" per line; blank lines and lines without '=' are ignored. Names are case-insensitive.</summary>
    public static Dictionary<string, string> Parse(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq].Trim().Trim('{', '}');
            if (PlaceholderRx().IsMatch("{" + name + "}") && !name.Equals("key", StringComparison.OrdinalIgnoreCase)) map[name] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    public static string ToText(IReadOnlyDictionary<string, string> map) => string.Join(Environment.NewLine, map.Select(p => $"{p.Key} = {p.Value}"));

    /// <summary>The user's value, else a built-in one; null when the name is unknown (or is "key").</summary>
    public static string? Value(string name, IReadOnlyDictionary<string, string>? map, DateTime now) =>
        name.Equals("key", StringComparison.OrdinalIgnoreCase) ? null
        : map?.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } v ? v // the saved map may not be case-blind
        : BuiltInValue(name, now);

    /// <param name="escape">True for a URL: the value is percent-encoded, so "New York" or "a&amp;b" cannot break the address.</param>
    public static string Fill(string text, IReadOnlyDictionary<string, string>? map, bool escape, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        return PlaceholderRx().Replace(text, m => Value(m.Groups[1].Value, map, at) is { } v ? (escape ? Uri.EscapeDataString(v) : v) : m.Value);
    }

    /// <summary>Placeholders that nothing fills - what the user still has to give a value. {key} is never listed.</summary>
    public static List<string> Missing(string text, IReadOnlyDictionary<string, string>? map) =>
        [.. PlaceholderRx().Matches(text).Select(m => m.Groups[1].Value)
            .Where(n => !n.Equals("key", StringComparison.OrdinalIgnoreCase) && Value(n, map, DateTime.Now) is null).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Every placeholder in the text except {key} and the built-ins - the names worth a line in the Variables box.</summary>
    public static List<string> Names(string text) =>
        [.. PlaceholderRx().Matches(text).Select(m => m.Groups[1].Value)
            .Where(n => !n.Equals("key", StringComparison.OrdinalIgnoreCase) && !BuiltIn.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
}
