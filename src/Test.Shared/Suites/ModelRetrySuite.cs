namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The shared model-call retry policy and per-endpoint concurrency limit: transient failures (429, 5xx, a 500
    /// wrapping one) are retried with backoff that honors Retry-After, client errors are not, retries stop at the
    /// runner's MaxRetries with a typed exception, cancellation ends a wait, and in-flight requests never exceed the
    /// runner's MaxConcurrentRequests.
    /// </summary>
    public static class ModelRetrySuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ModelRetry",
                displayName: "Model-call retries and endpoint concurrency",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ModelRetry", "Embedding_SucceedsAfterTwo429s", "An embedding call succeeds after two 429 responses, in three requests",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.FailNext(2);
                                await WithProcessorAsync(stub, 5, 4, ct, async (processor, runnerId) =>
                                {
                                    List<List<float>> vectors = await processor.EmbedAsync(new List<string> { "hello" }, runnerId, ct);
                                    if (vectors.Count != 1 || vectors[0].Count == 0) throw new Exception("expected one vector");
                                });
                                if (stub.EmbedRequestCount != 3) throw new Exception("expected 3 embedding requests, got " + stub.EmbedRequestCount);
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "Summarization_SucceedsAfterWrapped500", "A summarization call succeeds after a 500 whose body says the endpoint is at capacity",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.ChatText = "A short summary.";
                                stub.FailureStatus = 500;
                                stub.FailNext(1);
                                await WithProcessorAsync(stub, 5, 4, ct, async (processor, runnerId) =>
                                {
                                    string summary = await processor.SummarizeAsync("Some long text to summarize.", null, runnerId, ct);
                                    if (summary != "A short summary.") throw new Exception("unexpected summary: " + summary);
                                });
                                if (stub.ChatRequestCount != 2) throw new Exception("expected 2 chat requests, got " + stub.ChatRequestCount);
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "RetryAfter_IsHonored", "A 429 with Retry-After: 2 delays the retry by at least two seconds",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.RetryAfterSeconds = 2;
                                stub.FailNext(1);
                                Stopwatch sw = Stopwatch.StartNew();
                                await WithProcessorAsync(stub, 5, 4, ct, async (processor, runnerId) =>
                                {
                                    await processor.EmbedAsync(new List<string> { "hello" }, runnerId, ct);
                                });
                                if (sw.Elapsed < TimeSpan.FromSeconds(1.9)) throw new Exception("the retry should wait for Retry-After, waited " + sw.Elapsed.TotalSeconds + " s");
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "ClientError_IsNotRetried", "A 400 is returned after one request and surfaces as ModelRequestRejectedException",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.FailureStatus = 400;
                                stub.FailNext(1);
                                await WithProcessorAsync(stub, 5, 4, ct, async (processor, runnerId) =>
                                {
                                    try
                                    {
                                        await processor.SummarizeAsync("text", null, runnerId, ct);
                                        throw new Exception("a 400 should fail the call");
                                    }
                                    catch (ModelRequestRejectedException)
                                    {
                                    }
                                });
                                if (stub.ChatRequestCount != 1) throw new Exception("a 400 must not be retried; requests " + stub.ChatRequestCount);
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "Retries_StopAtMaxRetries", "Retries stop at the runner's MaxRetries and surface ModelEndpointUnavailableException",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.FailNext(100);
                                await WithProcessorAsync(stub, 2, 4, ct, async (processor, runnerId) =>
                                {
                                    try
                                    {
                                        await processor.EmbedAsync(new List<string> { "hello" }, runnerId, ct);
                                        throw new Exception("an endpoint that keeps answering 429 should fail the call");
                                    }
                                    catch (ModelEndpointUnavailableException)
                                    {
                                    }
                                });
                                if (stub.EmbedRequestCount != 3) throw new Exception("MaxRetries 2 means 3 requests, got " + stub.EmbedRequestCount);
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "Cancellation_EndsBackoffWait", "Cancelling during a Retry-After wait ends the call promptly",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.RetryAfterSeconds = 30;
                                stub.FailNext(100);
                                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                                {
                                    cts.CancelAfter(500);
                                    Stopwatch sw = Stopwatch.StartNew();
                                    await WithProcessorAsync(stub, 5, 4, ct, async (processor, runnerId) =>
                                    {
                                        try
                                        {
                                            await processor.EmbedAsync(new List<string> { "hello" }, runnerId, cts.Token);
                                            throw new Exception("the call should be cancelled");
                                        }
                                        catch (OperationCanceledException)
                                        {
                                        }
                                    });
                                    if (sw.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("cancellation should end the wait promptly, took " + sw.Elapsed.TotalSeconds + " s");
                                }
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "Limiter_CapsInFlightRequests", "Six concurrent calls never put more than MaxConcurrentRequests (2) in flight at the endpoint",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.DelayMs = 300;
                                await WithProcessorAsync(stub, 5, 2, ct, async (processor, runnerId) =>
                                {
                                    Task[] calls = Enumerable.Range(0, 6).Select(i => (Task)processor.EmbedAsync(new List<string> { "text " + i }, runnerId, ct)).ToArray();
                                    await Task.WhenAll(calls);
                                });
                                if (stub.MaxConcurrent > 2) throw new Exception("at most 2 requests may be in flight, saw " + stub.MaxConcurrent);
                                if (stub.EmbedRequestCount != 6) throw new Exception("expected 6 requests, got " + stub.EmbedRequestCount);
                            }
                        }),

                    new TestCaseDescriptor("ModelRetry", "RetryHandler_ClassifiesStatuses", "Transient statuses are 408, 429, 502, 503, and 504 only",
                        executeAsync: ct =>
                        {
                            foreach (int s in new[] { 408, 429, 502, 503, 504 }) if (!TransientRetryHandler.IsTransientStatus(s)) throw new Exception(s + " should be transient");
                            foreach (int s in new[] { 400, 401, 403, 404, 422, 500 }) if (TransientRetryHandler.IsTransientStatus(s)) throw new Exception(s + " should not be transient on its own");
                            if (new ModelRunner { MaxRetries = 99 }.MaxRetries != 10 || new ModelRunner { MaxRetries = -1 }.MaxRetries != 0) throw new Exception("MaxRetries should clamp to [0, 10]");
                            return Task.CompletedTask;
                        })
                });
        }

        private static async Task WithProcessorAsync(StubModelServer stub, int maxRetries, int maxConcurrent, CancellationToken ct, Func<NativeSemanticProcessor, string, Task> body)
        {
            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
            {
                ModelRunner runner = await db.ModelRunners.CreateAsync(new ModelRunner
                {
                    Name = "stub-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Provider = ModelRunnerProviderEnum.Ollama,
                    BaseUrl = stub.BaseUrl,
                    ApiType = "Ollama",
                    Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Embedding, ModelCapabilityEnum.Completion },
                    DefaultModel = "stub",
                    DefaultEmbeddingModel = "stub",
                    MaxRetries = maxRetries,
                    MaxConcurrentRequests = maxConcurrent,
                    Active = true,
                    HealthCheckEnabled = false
                }, ct);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                NativeSemanticProcessor processor = new NativeSemanticProcessor(db, new Aes256Cipher("test-signing-key"), logging);
                await body(processor, runner.Id);
            }
        }
    }
}
