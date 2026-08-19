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

                            using (VerbexClient client = new VerbexClient("http://127.0.0.1:8600", "verbexadmin", "ten_x", "pneuma", handler: handler))
                            {
                                bool threw = false;
                                try
                                {
                                    await client.AddDocumentAsync("idx_x", "content", new Dictionary<string, string>(), ct);
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
                            FakeVectorRepository vectors = new FakeVectorRepository();
                            await vectors.UpsertVectorAsync("n_near", new float[] { 1f, 0f, 0f }, new Dictionary<string, string> { { "subjectId", "sub_1" } }, ct);
                            await vectors.UpsertVectorAsync("n_far", new float[] { 0f, 1f, 0f }, new Dictionary<string, string> { { "subjectId", "sub_1" } }, ct);
                            await vectors.UpsertVectorAsync("n_other", new float[] { 1f, 0f, 0f }, new Dictionary<string, string> { { "subjectId", "sub_2" } }, ct);

                            List<VectorSearchHit> hits = await vectors.SearchAsync(
                                new float[] { 1f, 0f, 0f }, 10, 0.0,
                                new Dictionary<string, string> { { "subjectId", "sub_1" } }, ct);

                            if (hits.Count != 2) throw new Exception("Expected 2 tag-filtered hits, got " + hits.Count);
                            if (hits[0].NodeId != "n_near") throw new Exception("The closest vector should rank first, got " + hits[0].NodeId);
                            foreach (VectorSearchHit hit in hits)
                            {
                                if (hit.NodeId == "n_other") throw new Exception("The tag filter should have excluded n_other");
                            }
                        }),

                    new TestCaseDescriptor("ExternalServices", "LiteGraphVectors_Live_UpsertAndSearch", "LiteGraph-backed vectors round-trip against a live LiteGraph (gated on PNEUMA_LIVE_STACK=1)",
                        executeAsync: async ct =>
                        {
                            // Gated: only runs against a live LiteGraph v7 stack. No-op otherwise so CI stays green.
                            if (Environment.GetEnvironmentVariable("PNEUMA_LIVE_STACK") != "1") return;

                            string baseUrl = "http://127.0.0.1:8701";
                            string tenant = "00000000-0000-0000-0000-000000000000";
                            string testSubjectId = "vectest_" + Guid.NewGuid().ToString("N");

                            using (LiteGraphClient graph = new LiteGraphClient(baseUrl, "litegraphadmin", tenant, null))
                            using (LiteGraphVectorRepository vectors = new LiteGraphVectorRepository(baseUrl, "litegraphadmin", tenant, graph))
                            {
                                GraphNode node = new GraphNode
                                {
                                    NodeType = "TestVectorNode",
                                    Name = "vec-test-" + Guid.NewGuid().ToString("N"),
                                    CanonicalName = "vec-test",
                                    Labels = new List<string> { "TestVectorNode" }
                                };
                                node.Tags["subjectId"] = testSubjectId;

                                GraphNode created = await graph.CreateNodeAsync(node, ct);
                                if (String.IsNullOrEmpty(created.Id)) throw new Exception("LiteGraph did not return a node id");

                                try
                                {
                                    float[] embedding = new float[] { 1f, 0f, 0f, 0f };
                                    await vectors.UpsertVectorAsync(created.Id, embedding, null, ct);

                                    List<VectorSearchHit> hits = await vectors.SearchAsync(embedding, 5, 0.5, null, ct);
                                    if (!hits.Exists(h => h.NodeId == created.Id && h.Score > 0.9))
                                    {
                                        throw new Exception("live cosine search did not return the upserted node with a high score; hits=" + hits.Count);
                                    }
                                }
                                finally
                                {
                                    // Cascade-delete the test node (and its vector) by its subject tag.
                                    await graph.DeleteBySubjectAsync(testSubjectId, ct);
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
                            using (VerbexClient erroring = new VerbexClient("http://127.0.0.1:8600", "verbexadmin", "ten_x", "pneuma", retryCount: 0, handler: unauthHandler))
                            using (LiteGraphClient down = new LiteGraphClient("http://127.0.0.1:8701", null, "ten_x", null, retryCount: 0, handler: downHandler))
                            {
                                List<IServiceProbe> probes = new List<IServiceProbe> { healthy, erroring, down };
                                ExternalServiceDiagnosticsService diagnostics = new ExternalServiceDiagnosticsService(probes, logging);
                                List<IntegrationHealthResult> results = await diagnostics.RunAsync(false, ct);

                                IntegrationHealthResult doc = results.Find(r => r.ServiceName == "documentatom") ?? throw new Exception("Missing documentatom result");
                                IntegrationHealthResult vbx = results.Find(r => r.ServiceName == "verbex") ?? throw new Exception("Missing verbex result");
                                IntegrationHealthResult lgr = results.Find(r => r.ServiceName == "litegraph") ?? throw new Exception("Missing litegraph result");

                                if (!doc.Reachable || !doc.Success) throw new Exception("A 200 service should be reachable and healthy");
                                if (!vbx.Reachable || vbx.Success || vbx.StatusCode != 401) throw new Exception("A 401 service should be reachable but not successful");
                                if (lgr.Reachable) throw new Exception("A connection failure should be classified unreachable");
                            }
                        })
                });
        }
    }
}
