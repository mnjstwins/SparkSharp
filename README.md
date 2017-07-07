# SparkSharp
C# Livy client to submit Spark jobs to HDInsight and other Spark clusters

It contains also a snippet to run Spark SQL on Cosmos DB and return the results

Example usages:

```csharp
using (var client = new HdInsightClient("clusterName", "admin", "password"))
using (var session = await client.CreateSessionAsync(config))
{
    var sum = await session.ExecuteStatementAsync<int>("val res = 1 + 1\nprintln(res)");

    // Prints 2
    Console.WriteLine(sum);

    // Group by on Cosmos, yeah!
    const string sql = "SELECT id, SUM(json.total) AS total FROM cosmos GROUP BY id";

    var cosmos = await session.ExecuteCosmosDbSparkSqlQueryAsync<IEnumerable<Result>>
    (
        "cosmosName",
        "cosmosKey",
        "cosmosDatabase",
        "cosmosCollection",
        "cosmosPreferredRegions",
        sql
    );
    
    // Prints all the records resulting from the query and mapped to Result
    cosmos.ToList().ForEach(t => Console.WriteLine($"{t.id}:{t.total}"));
}
```