using RagService.Clients;
using RagService.Pipeline;

namespace RagService.Tests;

public class EvidenceFormatterTests
{
    [Fact]
    public void EvidenceFormatter_TriplesAndDisplayNames()
    {
        Assert.Equal("Brute Force", EvidenceFormatter.DisplayName(Evidence.Node("1", "Threat", new { id = "T1110", name = "Brute Force" })));
        Assert.Equal("Password Reset", EvidenceFormatter.DisplayName(Evidence.Node("1", "Document", new { id = "DOC-1", title = "Password Reset" })));
        Assert.Equal("M1032", EvidenceFormatter.DisplayName(Evidence.Node("1", "Control", new { id = "M1032", name = "" })));
        Assert.Equal("GET /api/x", EvidenceFormatter.DisplayName(Evidence.Node("1", "Endpoint", new { key = "GET /api/x" })));
        Assert.Equal("a1", EvidenceFormatter.DisplayName(Evidence.Node("1", "Alert", new { alertId = "a1", severity = "HIGH" })));
        Assert.Equal("inc-1", EvidenceFormatter.DisplayName(Evidence.Node("1", "Incident", new { incidentId = "inc-1" })));
        Assert.Equal("e1", EvidenceFormatter.DisplayName(Evidence.Node("1", "Event", new { eventId = "e1" })));
        Assert.Equal("u1", EvidenceFormatter.DisplayName(Evidence.Node("1", "User", new { userId = "u1" })));
        Assert.Equal("Role", EvidenceFormatter.DisplayName(Evidence.Node("1", "Role", new { other = 3 })));

        var triples = EvidenceFormatter.Triples(Evidence.Nodes(), Evidence.Rels());
        Assert.Equal(4, triples.Count);
        Assert.Equal("a1 —INDICATES→ Brute Force", triples[0]);
        Assert.Equal("Multi-factor Authentication —MITIGATES→ Brute Force", triples[1]);

        // A relationship whose endpoint is not among the nodes falls back to the raw element id.
        var orphan = EvidenceFormatter.Triples([], [new EvidenceRel("r", "DEPENDS_ON", "4:x:9", "4:x:8")]);
        Assert.Equal("4:x:9 —DEPENDS_ON→ 4:x:8", orphan.Single());
    }

    [Fact]
    public void Citations_BracketedTokens_DistinctInOrder()
    {
        var c = EvidenceFormatter.Citations("Alert [a1] indicates [T1110] and [T1110]; see [DOC-X, M1032] and [ M1018 ].");
        Assert.Equal(["a1", "T1110", "DOC-X", "M1032", "M1018"], c);
        Assert.Empty(EvidenceFormatter.Citations("no citations here"));
    }

    [Fact]
    public void CitationId_PrefersStableIdentifier()
    {
        Assert.Equal("DOC-1", EvidenceFormatter.CitationId(Evidence.Node("e", "Document", new { title = "T", id = "DOC-1" })));
        Assert.Equal("a1", EvidenceFormatter.CitationId(Evidence.Node("e", "Alert", new { alertId = "a1" })));
        Assert.Equal("identity-service", EvidenceFormatter.CitationId(Evidence.Node("e", "Service", new { name = "identity-service" })));
        Assert.Equal("e", EvidenceFormatter.CitationId(Evidence.Node("e", "Role", new { })));
    }

    [Fact]
    public void Sources_AreDocumentNodes()
    {
        var s = EvidenceFormatter.Sources(Evidence.Nodes());
        Assert.Equal(["DOC-PASSWORD-RESET", "DOC-T1110"], s.Select(x => x.Id));
        Assert.Null(s[0].Url);
        Assert.Equal("https://attack.mitre.org/techniques/T1110/", s[1].Url);
    }

    [Fact]
    public void NodeLines_CiteIdLabelAndTrimmedProperties()
    {
        var lines = EvidenceFormatter.NodeLines([Evidence.Node("e", "Threat", new { id = "T1110", name = "Brute Force", description = new string('x', 1000) })]);
        var line = Assert.Single(lines);
        Assert.StartsWith("[T1110] Threat:", line);
        Assert.Contains("name=\"Brute Force\"", line);
        Assert.True(line.Length < 500);
    }
}
