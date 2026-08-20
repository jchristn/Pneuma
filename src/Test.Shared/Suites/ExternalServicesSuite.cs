namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Server.Services;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the resilient integration client base: transient-failure retry on reads and the
    /// no-retry guarantee for writes. Drives the real client classes through an injected message
    /// handler, so the retry/timeout/bulkhead transport is exercised without a live service.
    /// </summary>
    public static class ExternalServicesSuite
    {
        /// <summary>Build the external-services suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ExternalServices",
                displayName: "External Services",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ExternalServices", "Retry_On503_ThenSucceeds", "A read retries transient 503s and then succeeds",
                        executeAsync: async ct =>
                        {
                            SequencedHttpMessageHandler handler = new SequencedHttpMessageHandler(attempt =>
                                attempt < 3
                                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Type\":\"Text\"}") });

                            using (DocumentAtomClient client = new DocumentAtomClient("http://127.0.0.1:8000", handler: handler))
                            {
                                TypeDetectResult result = await client.DetectTypeAsync(new byte[] { 1, 2, 3 }, ct);
                                if (result == null) throw new Exception("Expected a result after the transient failures cleared");
                                if (handler.CallCount != 3) throw new Exception("Expected 3 attempts (2 retries), got " + handler.CallCount);
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "Write_NotRetried_On503", "A write is not retried on transient failure",
                        executeAsync: async ct =>
                        {
                            SequencedHttpMessageHandler handler = new SequencedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

                            using (RecallDbClient client = new RecallDbClient("http://127.0.0.1:8600", "recalldbadmin", "pneuma", handler: handler))
                            {
                                bool threw = false;
                                try
                                {
                                    await client.StoreChunksAsync("ten_x", "col_x", new List<ChunkDocument>
                                    {
                                        new ChunkDocument { DocumentKey = "k1", DocumentId = "d1", Position = 0, Content = "content", Embedding = new List<float> { 1f }, Tags = new Dictionary<string, string>() }
                                    }, ct);
                                }
                                catch (IntegrationClientException)
                                {
                                    threw = true;
                                }

                                if (!threw) throw new Exception("Expected IntegrationClientException on a persistent 503");
                                if (handler.CallCount != 1) throw new Exception("A write must not retry; expected 1 attempt, got " + handler.CallCount);
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "Read_SurfacesUniformException", "A non-success read surfaces a structured IntegrationClientException",
                        executeAsync: async ct =>
                        {
                            SequencedHttpMessageHandler handler = new SequencedHttpMessageHandler(_ =>
                                new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad input") });

                            using (DocumentAtomClient client = new DocumentAtomClient("http://127.0.0.1:8000", handler: handler))
                            {
                                IntegrationClientException? captured = null;
                                try
                                {
                                    await client.DetectTypeAsync(new byte[] { 9 }, ct);
                                }
                                catch (IntegrationClientException exception)
                                {
                                    captured = exception;
                                }

                                if (captured == null) throw new Exception("Expected an IntegrationClientException on a 400 response");
                                if (captured.ServiceName != "documentatom") throw new Exception("Exception should carry the service name");
                                if (captured.StatusCode != 400) throw new Exception("Exception should carry the status code");
                                if (handler.CallCount != 1) throw new Exception("A 400 is not transient and must not be retried");
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "Timeout_CancelsSlowAttempt", "A read that exceeds the per-attempt timeout is cancelled rather than hanging",
                        executeAsync: async ct =>
                        {
                            // Handler stalls ~3s; the client's per-attempt timeout is the 1000ms floor and retries are off.
                            DelayingHttpMessageHandler handler = new DelayingHttpMessageHandler(3000,
                                () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Type\":\"Text\"}") });

                            using (DocumentAtomClient client = new DocumentAtomClient("http://127.0.0.1:8000", timeoutMilliseconds: 1000, retryCount: 0, handler: handler))
                            {
                                bool cancelled = false;
                                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                                try
                                {
                                    await client.DetectTypeAsync(new byte[] { 1 }, ct);
                                }
                                catch (OperationCanceledException)
                                {
                                    cancelled = true;
                                }
                                catch (IntegrationClientException)
                                {
                                    // A client that wraps the timeout in its uniform exception is equally acceptable.
                                    cancelled = true;
                                }
                                stopwatch.Stop();

                                if (!cancelled) throw new Exception("Expected the slow attempt to be cancelled by the per-attempt timeout");
                                if (stopwatch.Elapsed.TotalMilliseconds > 2500)
                                    throw new Exception("Timeout did not fire promptly; elapsed " + (int)stopwatch.Elapsed.TotalMilliseconds + "ms");
                                if (handler.CallCount != 1) throw new Exception("With retries off the request must be attempted once, got " + handler.CallCount);
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "ConcurrencyGate_CapsInFlight", "The concurrency gate caps simultaneous in-flight requests to a service",
                        executeAsync: async ct =>
                        {
                            // Each response stalls 200ms; with the gate capped at 1, three overlapping reads must serialize.
                            DelayingHttpMessageHandler handler = new DelayingHttpMessageHandler(200,
                                () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Type\":\"Text\"}") });

                            using (DocumentAtomClient client = new DocumentAtomClient("http://127.0.0.1:8000", maxConcurrentRequests: 1, retryCount: 0, handler: handler))
                            {
                                List<Task> calls = new List<Task>();
                                for (int i = 0; i < 3; i++)
                                {
                                    calls.Add(client.DetectTypeAsync(new byte[] { (byte)i }, ct));
                                }
                                await Task.WhenAll(calls);

                                if (handler.CallCount != 3) throw new Exception("Expected all 3 requests to run, got " + handler.CallCount);
                                if (handler.MaxInFlight != 1)
                                    throw new Exception("The gate should have limited in-flight requests to 1, observed " + handler.MaxInFlight);
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "VectorRepository_RanksByCosineAndFiltersByTag", "The vector store ranks by cosine similarity and honors a tag filter",
                        executeAsync: async ct =>
                        {
                            FakeRecallDbClient vectors = new FakeRecallDbClient();
                            RecallCollection collection = await vectors.CreateCollectionAsync("ten_x", new RecallCollection { Name = "t", Dimensionality = 3 }, ct);
                            await vectors.StoreChunksAsync("ten_x", collection.Id, new List<ChunkDocument>
                            {
                                new ChunkDocument { DocumentKey = "near", DocumentId = "d", Position = 0, Content = "", Embedding = new List<float> { 1f, 0f, 0f }, Tags = new Dictionary<string, string> { { "litegraphNodeId", "n_near" }, { "subjectId", "sub_1" } } },
                                new ChunkDocument { DocumentKey = "far", DocumentId = "d", Position = 1, Content = "", Embedding = new List<float> { 0f, 1f, 0f }, Tags = new Dictionary<string, string> { { "litegraphNodeId", "n_far" }, { "subjectId", "sub_1" } } },
                                new ChunkDocument { DocumentKey = "other", DocumentId = "d", Position = 2, Content = "", Embedding = new List<float> { 1f, 0f, 0f }, Tags = new Dictionary<string, string> { { "litegraphNodeId", "n_other" }, { "subjectId", "sub_2" } } }
                            }, ct);

                            List<VectorSearchHit> hits = await vectors.SearchAsync(
                                "ten_x", collection.Id, new float[] { 1f, 0f, 0f }, 10, 0.0,
                                new Dictionary<string, string> { { "subjectId", "sub_1" } }, ct);

                            if (hits.Count != 2) throw new Exception("Expected 2 tag-filtered hits, got " + hits.Count);
                            if (hits[0].NodeId != "n_near") throw new Exception("The closest vector should rank first, got " + hits[0].NodeId);
                            foreach (VectorSearchHit hit in hits)
                            {
                                if (hit.NodeId == "n_other") throw new Exception("The tag filter should have excluded n_other");
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "LiteGraph_ReadRequestsFullNode_AndRoundTripsDataTags", "Node reads request incldata/inclsub and map Data, Tags, and Labels back",
                        executeAsync: async ct =>
                        {
                            // A LiteGraph node as v7 returns it once the include flags are set.
                            string nodeJson = "{\"GUID\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Chunk label\",\"Labels\":[\"Chunk\"],\"Tags\":{\"subjectId\":\"sub_1\",\"sourceId\":\"src_1\"},\"Data\":{\"content\":\"Full chunk text.\",\"nodeType\":\"Chunk\"}}";
                            RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler(nodeJson);

                            using (LiteGraphClient client = new LiteGraphClient("http://127.0.0.1:8701", null, "00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000", retryCount: 0, handler: handler))
                            {
                                GraphNode? node = await client.ReadNodeAsync("11111111-1111-1111-1111-111111111111", ct);

                                if (handler.LastRequestUri == null || handler.LastRequestUri.IndexOf("incldata=true", StringComparison.Ordinal) < 0 || handler.LastRequestUri.IndexOf("inclsub=true", StringComparison.Ordinal) < 0)
                                    throw new Exception("Node read must request incldata=true&inclsub=true; got " + (handler.LastRequestUri ?? "<none>"));
                                if (node == null) throw new Exception("Expected the node to map");
                                if (node.Content != "Full chunk text.") throw new Exception("Data.content should round-trip; got " + (node.Content ?? "<null>"));
                                if (node.NodeType != "Chunk") throw new Exception("Data.nodeType/label should round-trip; got '" + node.NodeType + "'");
                                if (!node.Tags.TryGetValue("subjectId", out string? sub) || sub != "sub_1") throw new Exception("Tags should round-trip");
                                if (!node.Labels.Contains("Chunk")) throw new Exception("Labels should round-trip");
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "RecallDbVectors_Live_StoreAndSearch", "RecallDB-backed vectors round-trip against a live RecallDB (gated on PNEUMA_LIVE_STACK=1)",
                        executeAsync: async ct =>
                        {
                            // Gated: only runs against a live RecallDB stack. No-op otherwise so CI stays green.
                            if (Environment.GetEnvironmentVariable("PNEUMA_LIVE_STACK") != "1") return;

                            using (RecallDbClient recall = new RecallDbClient("http://127.0.0.1:8600", "recalldbadmin", "pneuma"))
                            {
                                await recall.EnsureTenantAsync("pneuma", "Pneuma", ct);
                                RecallCollection collection = await recall.CreateCollectionAsync("pneuma", new RecallCollection { Name = "livetest_" + Guid.NewGuid().ToString("N"), Dimensionality = 4 }, ct);
                                try
                                {
                                    string nodeId = "node_" + Guid.NewGuid().ToString("N");
                                    await recall.StoreChunksAsync("pneuma", collection.Id, new List<ChunkDocument>
                                    {
                                        new ChunkDocument { DocumentKey = "k_" + Guid.NewGuid().ToString("N"), DocumentId = "lnk_live", Position = 0, Content = "hello world", Embedding = new List<float> { 1f, 0f, 0f, 0f }, Tags = new Dictionary<string, string> { { "litegraphNodeId", nodeId } } }
                                    }, ct);

                                    List<VectorSearchHit> hits = await recall.SearchAsync("pneuma", collection.Id, new float[] { 1f, 0f, 0f, 0f }, 5, 0.5, null, ct);
                                    if (!hits.Exists(h => h.NodeId == nodeId && h.Score > 0.9))
                                    {
                                        throw new Exception("live cosine search did not return the stored chunk with a high score; hits=" + hits.Count);
                                    }
                                }
                                finally
                                {
                                    await recall.DeleteCollectionAsync("pneuma", collection.Id, ct);
                                }
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "Diagnostics_ClassifyReachability", "Startup diagnostics classify healthy, erroring, and unreachable services",
                        executeAsync: async ct =>
                        {
                            SequencedHttpMessageHandler okHandler = new SequencedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
                            SequencedHttpMessageHandler unauthHandler = new SequencedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
                            SequencedHttpMessageHandler downHandler = new SequencedHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));

                            LoggingModule logging = new LoggingModule();
                            logging.Settings.EnableConsole = false;

                            using (DocumentAtomClient healthy = new DocumentAtomClient("http://127.0.0.1:8000", retryCount: 0, handler: okHandler))
                            using (RecallDbClient erroring = new RecallDbClient("http://127.0.0.1:8600", "recalldbadmin", "pneuma", retryCount: 0, handler: unauthHandler))
                            using (LiteGraphClient down = new LiteGraphClient("http://127.0.0.1:8701", null, "ten_x", null, retryCount: 0, handler: downHandler))
                            {
                                List<IServiceProbe> probes = new List<IServiceProbe> { healthy, erroring, down };
                                ExternalServiceDiagnosticsService diagnostics = new ExternalServiceDiagnosticsService(probes, logging);
                                List<IntegrationHealthResult> results = await diagnostics.RunAsync(false, ct);

                                IntegrationHealthResult doc = results.Find(r => r.ServiceName == "documentatom") ?? throw new Exception("Missing documentatom result");
                                IntegrationHealthResult rcl = results.Find(r => r.ServiceName == "recalldb") ?? throw new Exception("Missing recalldb result");
                                IntegrationHealthResult lgr = results.Find(r => r.ServiceName == "litegraph") ?? throw new Exception("Missing litegraph result");

                                if (!doc.Reachable || !doc.Success) throw new Exception("A 200 service should be reachable and healthy");
                                if (!rcl.Reachable || rcl.Success || rcl.StatusCode != 401) throw new Exception("A 401 service should be reachable but not successful");
                                if (lgr.Reachable) throw new Exception("A connection failure should be classified unreachable");
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "TenantProvisioning_EnsuresTenantAndDefaultCollection", "Provisioning a tenant ensures its RecallDB tenant and a single default collection (idempotent)",
                        executeAsync: async ct =>
                        {
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            LoggingModule logging = new LoggingModule();
                            logging.Settings.EnableConsole = false;
                            TenantProvisioningService provisioning = new TenantProvisioningService(
                                new List<ITenantProvisioner> { new RecallDbTenantProvisioner(recall, "default", 384) }, logging);

                            await provisioning.ProvisionAsync("ten_a", "Tenant A", ct);
                            if (!recall.TenantExists("ten_a")) throw new Exception("tenant was not ensured");
                            List<RecallCollection> first = await recall.ListCollectionsAsync("ten_a", ct);
                            if (first.Count != 1 || first[0].Name != "default") throw new Exception("expected exactly one default collection, got " + first.Count);
                            if (first[0].Dimensionality != 384) throw new Exception("default collection dimensionality should be 384");

                            // Idempotent: a second pass must not create a duplicate default collection.
                            await provisioning.ProvisionAsync("ten_a", "Tenant A", ct);
                            List<RecallCollection> second = await recall.ListCollectionsAsync("ten_a", ct);
                            if (second.Count != 1) throw new Exception("provisioning must be idempotent; got " + second.Count + " collections");
                        }),

                    new TestCaseDescriptor("ExternalServices", "Collections_AreTenantIsolated", "A collection created in one tenant is not visible from another tenant",
                        executeAsync: async ct =>
                        {
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            await recall.EnsureTenantAsync("ten_a", "A", ct);
                            await recall.EnsureTenantAsync("ten_b", "B", ct);
                            RecallCollection created = await recall.CreateCollectionAsync("ten_a", new RecallCollection { Name = "kb", Dimensionality = 8 }, ct);

                            List<RecallCollection> tenantA = await recall.ListCollectionsAsync("ten_a", ct);
                            if (!tenantA.Exists(c => c.Id == created.Id)) throw new Exception("tenant A should see its own collection");
                            List<RecallCollection> tenantB = await recall.ListCollectionsAsync("ten_b", ct);
                            if (tenantB.Count != 0) throw new Exception("tenant B must not see tenant A's collections");
                            if (await recall.CollectionExistsAsync("ten_b", created.Id, ct)) throw new Exception("tenant B must not resolve tenant A's collection by id");
                            if (!await recall.CollectionExistsAsync("ten_a", created.Id, ct)) throw new Exception("tenant A should resolve its own collection by id");
                        })
                });
        }
    }
}
