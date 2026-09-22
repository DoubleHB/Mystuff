using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiScout.Services;

/// <summary>Turns a JSON sample into C# classes for System.Text.Json (one class per object shape).</summary>
public static partial class JsonToCSharp
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}")]
    private static partial Regex IsoDateRx();

    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
        "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    ];

    private sealed class ClassDef(string name)
    {
        public string Name { get; } = name;
        public List<(string Json, string Prop, string Type)> Props { get; } = [];
        /// <summary>Only there to keep the name taken (the client class itself).</summary>
        public bool Hidden { get; init; }
    }

    /// <summary>Null when <paramref name="json"/> is not valid JSON or holds no object to model.</summary>
    public static string? Generate(string json, string rootName = "Root")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var classes = new List<ClassDef>();
            var rootType = TypeOf([doc.RootElement], Pascal(rootName), classes);
            if (classes.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine();
            sb.AppendLine($"// var data = JsonSerializer.Deserialize<{rootType.TrimEnd('?')}>(json);");
            sb.AppendLine("// Types are inferred from this one response: widen int to double where a value can have decimals,");
            sb.AppendLine("// and add ? to anything the API may leave out.");
            Render(sb, classes);
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>The classes several JSON samples need, with names kept unique across all of them (used by the client generator).</summary>
    internal sealed class ClassSet
    {
        private readonly List<ClassDef> _classes = [];
        public bool IsEmpty => _classes.Count == 0;

        /// <summary>The C# type for this sample ("SearchResponse", "List&lt;Item&gt;", "int"…); null when it is not valid JSON.</summary>
        public string? Add(string json, string rootName)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                return TypeOf([doc.RootElement], Pascal(rootName), _classes);
            }
            catch (JsonException) { return null; }
        }

        public void Reserve(string name) => _classes.Add(new ClassDef(name) { Hidden = true });

        public string Render(string indent = "")
        {
            var sb = new StringBuilder();
            JsonToCSharp.Render(sb, _classes, indent);
            return sb.ToString();
        }
    }

    private static void Render(StringBuilder sb, List<ClassDef> classes, string indent = "")
    {
        foreach (var c in classes.Where(c => !c.Hidden))
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}public sealed class {c.Name}");
            sb.AppendLine(indent + "{");
            foreach (var (jsonName, prop, type) in c.Props)
            {
                sb.AppendLine($"{indent}    [JsonPropertyName(\"{jsonName.Replace("\\", "\\\\").Replace("\"", "\\\"")}\")]");
                var init = type.EndsWith('?') ? "" : type.StartsWith("List<") ? " = [];" : type == "string" ? " = \"\";"
                    : classes.Any(k => k.Name == type) ? " = new();" : "";
                sb.AppendLine($"{indent}    public {type} {prop} {{ get; set; }}{init}");
            }
            sb.AppendLine(indent + "}");
        }
    }

    /// <summary>The C# type that fits every sample (all values seen for one property, or all items of one array).</summary>
    private static string TypeOf(List<JsonElement> samples, string nameHint, List<ClassDef> classes)
    {
        bool nullable = samples.Count == 0 || samples.Any(s => s.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        var values = samples.Where(s => s.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)).ToList();
        if (values.Count == 0) return "object?";

        var kinds = values.Select(v => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? JsonValueKind.True : v.ValueKind).Distinct().ToList();
        if (kinds.Count > 1) return "JsonElement" + (nullable ? "?" : "");

        string type;
        switch (kinds[0])
        {
            case JsonValueKind.String:
                type = values.All(v => IsoDateRx().IsMatch(v.GetString() ?? "") && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    ? "DateTimeOffset" : "string";
                break;
            case JsonValueKind.Number:
                type = values.All(v => v.TryGetInt32(out _)) ? "int" : values.All(v => v.TryGetInt64(out _)) ? "long" : "double";
                break;
            case JsonValueKind.True:
                type = "bool";
                break;
            case JsonValueKind.Array:
                var items = values.SelectMany(v => v.EnumerateArray()).ToList();
                return $"List<{TypeOf(items, Singular(nameHint), classes)}>" + (nullable ? "?" : "");
            default: // Object
                type = AddClass(values, nameHint, classes);
                break;
        }
        return type + (nullable ? "?" : "");
    }

    private static string AddClass(List<JsonElement> objects, string nameHint, List<ClassDef> classes)
    {
        var name = nameHint;
        for (int n = 2; classes.Any(c => c.Name == name); n++) name = nameHint + n;
        var def = new ClassDef(name);
        classes.Add(def); // reserve the slot so the root class comes first

        var order = new List<string>();
        var byName = new Dictionary<string, List<JsonElement>>();
        foreach (var o in objects)
            foreach (var p in o.EnumerateObject())
            {
                if (!byName.TryGetValue(p.Name, out var list)) { byName[p.Name] = list = []; order.Add(p.Name); }
                list.Add(p.Value);
            }

        var used = new HashSet<string> { name };
        foreach (var jsonName in order)
        {
            var samples = byName[jsonName];
            var prop = Pascal(jsonName);
            for (int n = 2; !used.Add(prop); n++) prop = Pascal(jsonName) + n;
            var type = TypeOf(samples, Pascal(jsonName), classes);
            // missing from some objects of the same shape = optional
            if (samples.Count < objects.Count && !type.EndsWith('?')) type += "?";
            def.Props.Add((jsonName, prop, type));
        }
        return name;
    }

    internal static bool IsKeyword(string s) => Keywords.Contains(s);

    internal static string Pascal(string s)
    {
        var parts = Regex.Split(s, @"[^\p{L}\p{N}]+").Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "Value";
        var name = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        if (char.IsDigit(name[0])) name = "_" + name;
        return Keywords.Contains(name) ? "@" + name : name;
    }

    internal static string Singular(string s) =>
        s.EndsWith("ies", StringComparison.Ordinal) && s.Length > 4 ? s[..^3] + "y" :
        s.EndsWith("ses", StringComparison.Ordinal) || s.EndsWith("xes", StringComparison.Ordinal) ? s[..^2] :
        s.EndsWith('s') && !s.EndsWith("ss", StringComparison.Ordinal) && s.Length > 3 ? s[..^1] : s + "Item";
}
