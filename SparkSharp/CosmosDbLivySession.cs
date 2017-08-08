﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SparkSharp
{
    public class CosmosDbLivySession : IDisposable, ISparkSqlSession
    {
        readonly CosmosCollectionSettings _settings;
        readonly Lazy<Task<ILivySession>> _session;
        volatile bool _sessionInitialized;
        static int _sessionCount;

        public CosmosDbLivySession(ILivyClient client, CosmosCollectionSettings settings, LivySessionConfiguration livyConfig)
        {
            _settings = settings;
            _session = new Lazy<Task<ILivySession>>(() => CreateSessionAsync(client, livyConfig));
        }

        static Task<ILivySession> CreateSessionAsync(ILivyClient client, LivySessionConfiguration livyConfig)
        {
            var livySessionConfiguration = livyConfig.Clone();

            livySessionConfiguration.Name += " " + Interlocked.Increment(ref _sessionCount);

            return client.CreateSessionAsync(livySessionConfiguration);
        }

        /// <summary>
        /// Use cosmos as source.
        /// Example: SELECT * FROM cosmos
        /// </summary>
        public Task<TimedResult<IEnumerable<T>>> QuerySparkSqlWithMetricsAsync<T>(string sql) =>
            QuerySparkSqlWithMetricsAsync<T>(sql, null);
        
        public async Task<TimedResult<IEnumerable<T>>> QuerySparkSqlWithMetricsAsync<T>(string sparkSql, string cosmosSqlQuery)
        {
            var stopwatch = Stopwatch.StartNew();

            var results = await QuerySparkSqlAsync<T>(sparkSql, cosmosSqlQuery).ConfigureAwait(false);

            return new TimedResult<IEnumerable<T>> { Result = results, Elapsed = stopwatch.Elapsed };
        }

        public async Task WaitForSessionAsync()
        {
            var session = await _session.Value.ConfigureAwait(false);

            await session.WaitForSessionAsync().ConfigureAwait(false);
        }

        public async Task<bool> GetSessionAvailableAsync()
        {
            if (!_session.IsValueCreated)
                return false;

            var session = await _session.Value.ConfigureAwait(false);

            var state = await session.GetSessionStateAsync().ConfigureAwait(false);

            return state == "idle";
        }

        public Task<IEnumerable<T>> QuerySparkSqlAsync<T>(string sql) => 
            QuerySparkSqlAsync<T>(sql, null);

        /// <summary>
        /// Use cosmos as source.
        /// Example: SELECT * FROM cosmos
        /// </summary>
        /// <param name="sparkSqlQuery">The Spark SQL query</param>
        /// <param name="cosmosSqlQuery">The base Cosmos DB query with Document DB SQL; use a query that pulls only required data from cosmos, minimizing the network transfer between Spark nodes and Cosmos. If null, it uses SELECT * from data in Cosmos</param>
        public async Task<IEnumerable<T>> QuerySparkSqlAsync<T>(string sparkSqlQuery, string cosmosSqlQuery)
        {
            var session = await _session.Value.ConfigureAwait(false);

            string initializeContextCode = null;

            if(cosmosSqlQuery != null)
                initializeContextCode = GetInitializeContextCode(cosmosSqlQuery);
            else if (!_sessionInitialized)
                initializeContextCode = GetInitializeContextCode("SELECT * FROM c");

            var scalaCode = $@"
{initializeContextCode}

val docs = spark.sql(s""""""{sparkSqlQuery}"""""")

println(docs.toJSON.collect.mkString(""["", "","", ""]""))
";

            var results = await session.ExecuteStatementAsync<IEnumerable<T>>(scalaCode).ConfigureAwait(false);

            _sessionInitialized = true;

            return results;
        }

        string GetInitializeContextCode(string cosmosSqlQuery) => $@"
import com.microsoft.azure.cosmosdb.spark.schema._
import com.microsoft.azure.cosmosdb.spark._
import com.microsoft.azure.cosmosdb.spark.config.Config

val config = Config(Map(""Endpoint""         -> ""https://{_settings.Name}.documents.azure.com:443/"",
                        ""Masterkey""        -> ""{_settings.Key}"",
                        ""Database""         -> ""{_settings.Database}"",
                        ""preferredRegions"" -> ""{_settings.PreferredRegions};"",
                        ""Collection""       -> ""{_settings.Collection}"", 
                        ""SamplingRatio""    -> ""1.0"",
                        ""query_pagesize""   -> ""2147483647"",
                        ""query_custom""     -> s""""""{cosmosSqlQuery}""""""))

spark.sqlContext.read.cosmosDB(config).createOrReplaceTempView(""cosmos"")
";

        public void Dispose() => CloseAsync().GetAwaiter().GetResult();

        public async Task CloseAsync()
        {
            if (_session.IsValueCreated)
                await (await _session.Value.ConfigureAwait(false)).CloseAsync().ConfigureAwait(false);
        }
    }
}