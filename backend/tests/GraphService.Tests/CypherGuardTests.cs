using GraphService.Rag;

namespace GraphService.Tests;

public class CypherGuardTests
{
    [Theory]
    [InlineData("CREATE (n)", "CREATE")]
    [InlineData("MATCH (n) SET n.x=1 RETURN n", "SET")]
    [InlineData("MERGE (n:X) RETURN n", "MERGE")]
    [InlineData("MATCH (n) DETACH DELETE n", "DELETE|DETACH")]
    [InlineData("MATCH (n) REMOVE n.x RETURN n", "REMOVE")]
    [InlineData("DROP INDEX x", "DROP")]
    [InlineData("LOAD CSV FROM 'x' AS r RETURN r", "LOAD CSV")]
    [InlineData("MATCH (n) FOREACH (x IN [1] | SET n.a = x) RETURN n", "FOREACH")]
    [InlineData("CALL { MATCH (n) RETURN n } IN TRANSACTIONS RETURN 1", "TRANSACTIONS")]
    [InlineData("USE neo4j MATCH (n) RETURN n", "USE")]
    [InlineData("CALL dbms.components()", "dbms.components")]
    [InlineData("CALL db.createLabel('x')", "db.createLabel")]
    [InlineData("RETURN apoc.create.node(['X'],{})", "apoc.create.node")]
    [InlineData("match (n) delete n", "DELETE")]
    [InlineData("INSERT (n:Pwn) RETURN n", "INSERT")]
    [InlineData("SHOW SETTINGS", "SHOW")]
    [InlineData("SHOW TRANSACTIONS", "SHOW")]
    [InlineData("TERMINATE TRANSACTIONS 'x' RETURN 1", "TERMINATE")]
    [InlineData("GRANT ROLE x TO y", "GRANT")]
    [InlineData("DENY MATCH {*} ON GRAPH * TO r", "DENY")]
    [InlineData("REVOKE ROLE x FROM y", "REVOKE")]
    [InlineData("ALTER USER u SET PASSWORD 'x'", "ALTER")]
    [InlineData("START DATABASE foo", "START")]
    [InlineData("STOP DATABASE foo", "STOP")]
    [InlineData("RENAME ROLE a TO b", "RENAME")]
    [InlineData("ENABLE SERVER 'x'", "ENABLE")]
    [InlineData("DEALLOCATE DATABASES FROM SERVER 'x'", "DEALLOCATE")]
    [InlineData("REALLOCATE DATABASES", "REALLOCATE")]
    [InlineData("CALL { MATCH (n) RETURN n } IN 2 CONCURRENT TRANSACTIONS RETURN 1", "TRANSACTIONS")]
    [InlineData("CALL { MATCH (n) RETURN n } IN CONCURRENT TRANSACTIONS RETURN 1", "TRANSACTIONS")]
    [InlineData("CALL `db`.`createLabel`('x') RETURN 1", "db.createLabel")]
    [InlineData("CALL db . createLabel('x') RETURN 1", "db.createLabel")]
    [InlineData("CALL db/**/.createLabel('x') RETURN 1", "db.createLabel")]
    [InlineData("CALL dbms . components() YIELD name RETURN name", "dbms.components")]
    [InlineData("RETURN `apoc`.create.node(['X'],{})", "apoc.create.node")]
    [InlineData("RETURN apoc .cypher.doIt('x',{})", "apoc.cypher.doIt")]
    [InlineData("CALL tx.setMetaData({a:1}) RETURN 1", "tx.setMetaData")]
    [InlineData("CALL apoc.convert.setJsonProperty(n,'a','b') RETURN 1", "apoc.convert.setJsonProperty")]
    [InlineData("CALL apoc.text.join(['a'],',') YIELD value RETURN value", "apoc.text.join")]
    [InlineData("RETURN apoc.convert.setJsonProperty(n,'a','b')", "apoc.convert.setJsonProperty")]
    [InlineData("RETURN other.ns.fn(1)", "other.ns.fn")]
    [InlineData("RETURN db.labels()", "db.labels")]
    [InlineData("RETURN dbms.components()", "dbms.components")]
    [InlineData("CALL (x) RETURN 1", "Procedure")]
    public void Guard_WriteOrAdmin_Rejected(string cypher, string expected)
    {
        var r = CypherGuard.Check(cypher);
        Assert.False(r.Allowed);
        Assert.True(expected.Split('|').Any(e => r.Reason!.Contains(e, StringComparison.OrdinalIgnoreCase)), r.Reason);
    }

    [Theory]
    [InlineData("CALL db.labels() YIELD label RETURN label")]
    [InlineData("CALL db.relationshipTypes() YIELD relationshipType RETURN relationshipType")]
    [InlineData("CALL db.propertyKeys() YIELD propertyKey RETURN propertyKey")]
    [InlineData("RETURN apoc.text.join(['a'], ',')")]
    [InlineData("RETURN apoc.coll.toSet([1,1])")]
    [InlineData("RETURN apoc.map.merge({a:1},{b:2})")]
    [InlineData("RETURN apoc.convert.toJson({a:1})")]
    [InlineData("CALL db.labels YIELD label RETURN label")]
    [InlineData("CALL `db`.`LABELS`() YIELD label RETURN label")]
    [InlineData("CALL { MATCH (n) RETURN n } RETURN n")]
    [InlineData("MATCH (n) CALL (n) { MATCH (n)-[]->(m) RETURN m } RETURN m")]
    [InlineData("MATCH (n) RETURN n.set, n.start, n.db, toUpper(n.name), db.name")]
    public void Guard_AllowListedProcedures_Allowed(string cypher)
        => Assert.True(CypherGuard.Check(cypher).Allowed);

    [Theory]
    [InlineData("MATCH (a:Alert) WHERE a.createdAt IS NOT NULL RETURN a.offset")]
    [InlineData("MATCH (d:Document {name:'Password Reset'}) RETURN d")]
    [InlineData("MATCH (n) WHERE n.description CONTAINS 'set' RETURN n")]
    [InlineData("MATCH (n) // delete everything\nRETURN n")]
    [InlineData("MATCH (n) /* DROP ; CREATE */ RETURN n")]
    [InlineData("MATCH (n) WHERE n.name = 'it\\'s; DELETE' RETURN n")]
    [InlineData("MATCH (n:`Set`) RETURN n")]
    [InlineData("MATCH (n) RETURN n SKIP 1 LIMIT 5")]
    public void Guard_KeywordsInsideIdentifiersOrStrings_Allowed(string cypher)
        => Assert.True(CypherGuard.Check(cypher).Allowed);

    [Fact]
    public void Guard_MultiStatement_Rejected()
    {
        var r = CypherGuard.Check("MATCH (n) RETURN n; MATCH (m) RETURN m");
        Assert.False(r.Allowed);
        Assert.Equal("Only a single statement is allowed", r.Reason);
    }

    [Fact]
    public void Guard_TrailingSemicolon_Allowed()
    {
        var r = CypherGuard.Check("MATCH (n) RETURN n LIMIT 3 ;  \n");
        Assert.True(r.Allowed);
        Assert.Equal("MATCH (n) RETURN n LIMIT 3", r.Cypher);
    }

    [Fact]
    public void Guard_SemicolonInsideString_Allowed()
        => Assert.True(CypherGuard.Check("RETURN 'a;b'").Allowed);

    [Fact]
    public void Guard_NoReturn_Rejected()
    {
        var r = CypherGuard.Check("MATCH (n)");
        Assert.False(r.Allowed);
        Assert.Equal("Query must contain RETURN", r.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ;  ")]
    [InlineData("// just a comment")]
    public void Guard_Empty_Rejected(string? cypher)
    {
        var r = CypherGuard.Check(cypher);
        Assert.False(r.Allowed);
        Assert.Equal("Query is empty", r.Reason);
    }

    [Fact]
    public void Guard_AppendsLimitOnlyWhenMissing()
    {
        Assert.EndsWith(" LIMIT 200", CypherGuard.Check("MATCH (n) RETURN n;").Cypher);
        Assert.Equal("MATCH (n) RETURN n LIMIT 5", CypherGuard.Check("MATCH (n) RETURN n LIMIT 5").Cypher);
        Assert.EndsWith("LIMIT 200", CypherGuard.Check("MATCH (n) WHERE n.k = 'limit' RETURN n").Cypher);
        Assert.EndsWith("LIMIT 50", CypherGuard.Check("RETURN 1", 50).Cypher);
        Assert.Equal("MATCH (n) RETURN n LIMIT 200", CypherGuard.Check("MATCH (n) RETURN n // c\n;").Cypher);
    }

    // Deliberate false positives: keywords as map keys / bare identifiers are rejected (conservative guard).
    [Theory]
    [InlineData("RETURN {set: 1}")]
    [InlineData("MATCH (start) RETURN start")]
    public void Guard_KeywordAsBareIdentifier_RejectedDeliberately(string cypher)
        => Assert.False(CypherGuard.Check(cypher).Allowed);
}
