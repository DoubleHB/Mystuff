namespace ApiScout.Services;

/// <summary>Line-by-line comparison of two responses (longest common subsequence).</summary>
public static class LineDiff
{
    public sealed record Result(string Text, int Added, int Removed);

    public static Result Compare(string older, string newer, int maxLines = 2500)
    {
        var a = older.Replace("\r", "").Split('\n');
        var b = newer.Replace("\r", "").Split('\n');
        if (a.Length > maxLines || b.Length > maxLines)
            return new($"These responses are too long to compare line by line ({a.Length:N0} and {b.Length:N0} lines).", 0, 0);

        // lcs[i, j] = length of the common subsequence of a[i..] and b[j..]
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
            for (int j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var sb = new System.Text.StringBuilder();
        int added = 0, removed = 0, x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { sb.Append("  ").AppendLine(a[x]); x++; y++; }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y])) { sb.Append("+ ").AppendLine(b[y]); y++; added++; }
            else { sb.Append("- ").AppendLine(a[x]); x++; removed++; }
        }
        return new(sb.ToString().TrimEnd(), added, removed);
    }
}
