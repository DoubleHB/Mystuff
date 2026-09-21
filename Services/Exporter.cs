using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApiScout.ViewModels;

namespace ApiScout.Services;

/// <summary>Turns one API or a whole filtered list into clipboard / file text.</summary>
public static class Exporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Text(ApiRow r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Name);
        if (r.Description.Length > 0) sb.AppendLine(r.Description);
        sb.AppendLine($"Category: {r.Category}");
        sb.AppendLine($"Auth: {r.AuthLabel}");
        sb.AppendLine($"Free access: {r.AccessLabel}");
        sb.AppendLine($"Docs: {r.Url}");
        if (r.SpecUrl is { Length: > 0 }) sb.AppendLine($"OpenAPI spec: {r.SpecUrl}");
        if (r.HasDemoKey) sb.AppendLine($"Demo key: {r.DemoKey}  ({r.KeyUsage})");
        if (r.SignupUrl is { Length: > 0 }) sb.AppendLine($"Get a key: {r.SignupUrl}");
        if (r.Example is { Length: > 0 }) sb.AppendLine($"Example: {r.Example}");
        return sb.ToString().TrimEnd();
    }

    public static string Markdown(ApiRow r)
    {
        var sb = new StringBuilder($"**[{r.Name}]({r.Url})** - {r.Description}  \n");
        sb.Append($"Category: {r.Category} · Auth: {r.AuthLabel}");
        if (r.HasDemoKey) sb.Append($" · Demo key: `{r.DemoKey}`");
        if (r.SignupUrl is { Length: > 0 }) sb.Append($" · [Get a key]({r.SignupUrl})");
        return sb.ToString();
    }

    public static string JsonOne(ApiRow r) => JsonSerializer.Serialize(Shape(r), Json);

    public static string Curl(ApiRow r) =>
        r.Example is { Length: > 0 } ex
            ? $"curl \"{ex}\"" + (r.KeyUsage?.StartsWith("Header: ") == true ? $" -H \"{r.KeyUsage[8..]}\"" : "")
            : $"curl -i \"{r.Url}\"";

    public static string CSharp(ApiRow r)
    {
        var url = r.Example is { Length: > 0 } ex ? ex : r.Url;
        var header = r.KeyUsage?.StartsWith("Header: ") == true && r.KeyUsage[8..].Split(": ", 2) is [var n, var v]
            ? $"http.DefaultRequestHeaders.Add(\"{n}\", \"{v}\");\n" : "";
        return "using var http = new HttpClient();\n" +
               "http.DefaultRequestHeaders.UserAgent.ParseAdd(\"MyApp/1.0\");\n" + header +
               $"var json = await http.GetStringAsync(\"{url}\");\n" +
               "Console.WriteLine(json);";
    }

    public static string MarkdownTable(IEnumerable<ApiRow> rows)
    {
        var sb = new StringBuilder("| API | Category | Auth | Demo key | Get a key | Description |\n|---|---|---|---|---|---|\n");
        foreach (var r in rows)
            sb.AppendLine($"| [{Pipe(r.Name)}]({r.Url}) | {r.Category} | {r.AuthLabel} | {(r.HasDemoKey ? $"`{r.DemoKey}`" : "")} | {r.SignupUrl} | {Pipe(r.Description)} |");
        return sb.ToString();
    }

    public static string Csv(IEnumerable<ApiRow> rows)
    {
        var sb = new StringBuilder("Name,Category,Auth,Free access,HTTPS,CORS,Demo key,Get a key,Docs URL,Description,Sources\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(',', new[] { r.Name, r.Category, r.AuthLabel, r.AccessLabel, r.HttpsLabel, r.Cors, r.DemoKey ?? "", r.SignupUrl ?? "", r.Url, r.Description, r.SourcesLabel }.Select(Quote))).Append("\r\n");
        return sb.ToString();
    }

    public static string JsonMany(IEnumerable<ApiRow> rows) => JsonSerializer.Serialize(rows.Select(Shape), Json);

    public static string Urls(IEnumerable<ApiRow> rows) => string.Join(Environment.NewLine, rows.Select(r => r.Url));

    private static object Shape(ApiRow r) => new
    {
        name = r.Name,
        brand = r.BrandDomain,
        description = r.Description,
        category = r.Category,
        auth = r.AuthLabel,
        freeAccess = r.AccessLabel,
        https = r.Entry.Https,
        cors = r.Cors,
        url = r.Url,
        openApiSpec = r.SpecUrl,
        demoKey = r.DemoKey,
        keyUsage = r.KeyUsage,
        getKeyUrl = r.SignupUrl,
        example = r.Example,
        sources = r.Entry.Sources,
    };

    private static string Pipe(string s) => s.Replace("|", "\\|");
    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
