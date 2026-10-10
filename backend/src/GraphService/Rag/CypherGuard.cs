using System.Text;
using System.Text.RegularExpressions;

namespace GraphService.Rag;

public sealed record CypherGuardResult(bool Allowed, string? Reason, string Cypher);

/// <summary>
/// Pure static guard for LLM-generated Cypher: accepts only a single read-only statement.
/// The read transaction in the executor is the second line of defence.
/// </summary>
public static class CypherGuard
{
    // Not preceded by '.', '$' or a word char, so property names (n.set) and parameters ($set) are ignored.
    private static readonly Regex Forbidden = new(
        @"(?<![\w.$])(CREATE|MERGE|DELETE|DETACH|SET|REMOVE|DROP|FOREACH|USE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LoadCsv = new(@"(?<![\w.$])LOAD\s+CSV\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InTransactions = new(@"\bIN\s+TRANSACTIONS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Qualified = new(@"(?<![\w.$])(dbms|db|apoc)\.([\w.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReturnKw = new(@"(?<![\w.$])RETURN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LimitKw = new(@"(?<![\w.$])LIMIT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] AllowedDb = ["labels", "relationshipTypes", "propertyKeys"];
    private static readonly string[] AllowedApocPrefixes = ["map.", "coll.", "text.", "convert."];

    public static CypherGuardResult Check(string? cypher, int defaultLimit = 200)
    {
        if (string.IsNullOrWhiteSpace(cypher)) return Reject("Query is empty", cypher);

        var scrubbed = Scrub(cypher);
        var end = scrubbed.Length;
        while (end > 0 && (char.IsWhiteSpace(scrubbed[end - 1]) || scrubbed[end - 1] == ';')) end--;
        if (end == 0) return Reject("Query is empty", cypher);

        var body = scrubbed[..end];
        var original = cypher[..end];

        if (body.Contains(';')) return Reject("Only a single statement is allowed", original);

        var m = Forbidden.Match(body);
        if (m.Success) return Reject($"Write or admin clause not allowed: {m.Value.ToUpperInvariant()}", original);
        if (LoadCsv.IsMatch(body)) return Reject("Write or admin clause not allowed: LOAD CSV", original);
        if (InTransactions.IsMatch(body)) return Reject("Write or admin clause not allowed: IN TRANSACTIONS", original);

        foreach (Match p in Qualified.Matches(body))
        {
            var ns = p.Groups[1].Value.ToLowerInvariant();
            var rest = p.Groups[2].Value;
            var ok = ns switch
            {
                "db" => AllowedDb.Contains(rest, StringComparer.OrdinalIgnoreCase),
                "apoc" => AllowedApocPrefixes.Any(a => rest.StartsWith(a, StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
            if (!ok) return Reject($"Procedure not allowed: {ns}.{rest}", original);
        }

        if (!ReturnKw.IsMatch(body)) return Reject("Query must contain RETURN", original);

        var final = LimitKw.IsMatch(body) ? original : $"{original} LIMIT {defaultLimit}";
        return new CypherGuardResult(true, null, final);
    }

    private static CypherGuardResult Reject(string reason, string? cypher) => new(false, reason, cypher ?? "");

    /// <summary>Replaces string literals, backtick identifiers and comments with spaces (length preserved).</summary>
    private static string Scrub(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n' && s[i] != '\r') { sb.Append(' '); i++; }
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? s.Length : close + 2;
                for (; i < stop; i++) sb.Append(s[i] is '\n' or '\r' ? s[i] : ' ');
            }
            else if (c is '\'' or '"' or '`')
            {
                sb.Append(c); i++;
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\\' && c != '`' && i + 1 < s.Length) { sb.Append(' '); i++; }
                    sb.Append(' '); i++;
                }
                if (i < s.Length) { sb.Append(c); i++; }
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }
}
