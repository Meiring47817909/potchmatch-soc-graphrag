using System.Text;

namespace GraphService.Rag;

public sealed record CypherGuardResult(bool Allowed, string? Reason, string Cypher);

/// <summary>
/// Pure static guard for LLM-generated Cypher: accepts only a single read-only statement.
/// The read transaction in the executor is the second line of defence.
/// <para>
/// The text is tokenised (strings, comments and whitespace dropped, backtick identifiers unquoted) and all
/// checks run on tokens, so qualified names are normalised (<c>`db` . createLabel</c> == <c>db.createLabel</c>).
/// </para>
/// <para>
/// LIMIT detection is token based and looks anywhere in the statement (also inside a subquery); a LIMIT in a
/// subquery does not bound the outer result, so the executor's 200-row cap is the real backstop.
/// </para>
/// <para>
/// Deliberate false positives: clause keywords used as bare identifiers or map keys (<c>{set: 1}</c>,
/// a variable called <c>start</c>) are rejected. Property access (<c>n.set</c>, <c>db.name</c>) is allowed.
/// </para>
/// </summary>
public static class CypherGuard
{
    private enum Kind { Word, Quoted, Str, Punct }
    private readonly record struct Tok(string Text, Kind Kind, int End);

    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "MERGE", "DELETE", "DETACH", "SET", "REMOVE", "DROP", "FOREACH", "USE", "INSERT",
        "SHOW", "TERMINATE", "GRANT", "DENY", "REVOKE", "ALTER", "START", "STOP", "RENAME", "ENABLE",
        "DEALLOCATE", "REALLOCATE", "TRANSACTIONS",
    };

    private static readonly HashSet<string> AllowedProcedures = new(StringComparer.OrdinalIgnoreCase)
    {
        "db.labels", "db.relationshipTypes", "db.propertyKeys",
    };

    private static readonly string[] AllowedApocPrefixes = ["apoc.map.", "apoc.coll.", "apoc.text.", "apoc.convert."];
    private const string DeniedApocFunction = "apoc.convert.setjsonproperty";

    public static CypherGuardResult Check(string? cypher, int defaultLimit = 200)
    {
        if (string.IsNullOrWhiteSpace(cypher)) return Reject("Query is empty", cypher);

        var toks = Tokenize(cypher);
        var count = toks.Count;
        while (count > 0 && IsPunct(toks[count - 1], ';')) count--;
        if (count == 0) return Reject("Query is empty", cypher);

        var original = cypher[..toks[count - 1].End];
        for (var i = 0; i < count; i++)
            if (IsPunct(toks[i], ';')) return Reject("Only a single statement is allowed", original);

        var hasReturn = false;
        var hasLimit = false;

        for (var i = 0; i < count; i++)
        {
            var t = toks[i];
            if (t.Kind != Kind.Word && t.Kind != Kind.Quoted) continue;
            if (i > 0 && (IsPunct(toks[i - 1], '.') || IsPunct(toks[i - 1], '$'))) continue;

            if (t.Kind == Kind.Word)
            {
                if (ForbiddenKeywords.Contains(t.Text))
                    return Reject($"Write or admin clause not allowed: {t.Text.ToUpperInvariant()}", original);
                if (t.Text.Equals("LOAD", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < count && toks[i + 1].Kind == Kind.Word
                    && toks[i + 1].Text.Equals("CSV", StringComparison.OrdinalIgnoreCase))
                    return Reject("Write or admin clause not allowed: LOAD CSV", original);
                if (t.Text.Equals("RETURN", StringComparison.OrdinalIgnoreCase)) hasReturn = true;
                if (t.Text.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)) hasLimit = true;

                if (t.Text.Equals("CALL", StringComparison.OrdinalIgnoreCase))
                {
                    var next = i + 1;
                    if (next < count && IsPunct(toks[next], '{')) continue; // CALL { ... } subquery
                    if (next < count && IsPunct(toks[next], '('))
                    {
                        var depth = 0; var j = next;
                        for (; j < count; j++)
                        {
                            if (IsPunct(toks[j], '(')) depth++;
                            else if (IsPunct(toks[j], ')') && --depth == 0) break;
                        }
                        if (j + 1 < count && IsPunct(toks[j + 1], '{')) continue; // CALL (x) { ... }
                        return Reject("Procedure not allowed: (expression)", original);
                    }
                    var (pname, _) = ReadChain(toks, count, next);
                    if (pname.Length == 0 || !AllowedProcedures.Contains(pname))
                        return Reject($"Procedure not allowed: {(pname.Length == 0 ? "(unnamed)" : pname)}", original);
                    i = next; // the name is vetted; skip its first token
                    continue;
                }
            }

            // Qualified function call: a.b(...) / a.b.c(...)
            var (name, end) = ReadChain(toks, count, i);
            if (name.Contains('.') && end < count && IsPunct(toks[end], '(') && !FunctionAllowed(name))
                return Reject($"Procedure not allowed: {name}", original);
            if (end > i + 1) i = end - 1;
        }

        if (!hasReturn) return Reject("Query must contain RETURN", original);
        return new CypherGuardResult(true, null, hasLimit ? original : $"{original} LIMIT {defaultLimit}");
    }

    private static bool FunctionAllowed(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower != DeniedApocFunction && AllowedApocPrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>Reads word ('.' word)* starting at <paramref name="start"/>; returns the dotted name and the index after it.</summary>
    private static (string Name, int End) ReadChain(List<Tok> toks, int count, int start)
    {
        var sb = new StringBuilder();
        var i = start;
        while (i < count && toks[i].Kind is Kind.Word or Kind.Quoted)
        {
            sb.Append(toks[i].Text);
            i++;
            if (i + 1 < count && IsPunct(toks[i], '.') && toks[i + 1].Kind is Kind.Word or Kind.Quoted)
            {
                sb.Append('.');
                i++;
            }
            else break;
        }
        return (sb.ToString(), i);
    }

    private static bool IsPunct(Tok t, char c) => t.Kind == Kind.Punct && t.Text[0] == c;

    private static CypherGuardResult Reject(string reason, string? cypher) => new(false, reason, cypher ?? "");

    private static List<Tok> Tokenize(string s)
    {
        var toks = new List<Tok>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? s.Length : close + 2;
            }
            else if (c is '\'' or '"')
            {
                i++;
                while (i < s.Length && s[i] != c) i += s[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, s.Length);
                toks.Add(new Tok("", Kind.Str, i));
            }
            else if (c == '`')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '`')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '`') { sb.Append('`'); i += 2; continue; }
                        break;
                    }
                    sb.Append(s[i++]);
                }
                i = Math.Min(i + 1, s.Length);
                toks.Add(new Tok(sb.ToString(), Kind.Quoted, i));
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                var st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                toks.Add(new Tok(s[st..i], Kind.Word, i));
            }
            else { i++; toks.Add(new Tok(c.ToString(), Kind.Punct, i)); }
        }
        return toks;
    }
}
