namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Models;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using Touchstone.Core;

    /// <summary>Exercises native xAI requests against the upstream Postman contract fixture.</summary>
    public static class PolyPromptSimulationSuite
    {
        /// <summary>Build the opt-in simulation suite. Start the saved Postman simulation first.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("PolyPromptSimulation", "PolyPrompt Simulation", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor("PolyPromptSimulation", "XAI_NativeRoundTrip", "Native streaming tools replay encrypted reasoning and preserve usage; embeddings preserve ordering",
                    executeAsync: async ct =>
                    {
                        string endpoint = Environment.GetEnvironmentVariable("PNEUMA_TEST_XAI_SIMULATION")
                            ?? throw new InvalidOperationException("Set PNEUMA_TEST_XAI_SIMULATION to the local Postman simulation URL.");
                        using (LoggingModule logging = new LoggingModule())
                        {
                            logging.Settings.EnableConsole = false;
                            ModelRunner runner = new ModelRunner { Name = "xai-simulation", Provider = ModelRunnerProviderEnum.XAI, BaseUrl = endpoint, DefaultModel = "test-model", DefaultEmbeddingModel = "test-embedding" };
                            CompletionClientBase client = ModelClientFactory.Create(runner, "fixture-key", logging);
                            ToolChatRequest request = new ToolChatRequest
                            {
                                Messages = new List<ChatMessage> { ChatMessage.User("xai-console-smoke") },
                                Tools = new List<ToolDefinition>
                                {
                                    new ToolDefinition { Name = "list_directory", Description = "List files", Parameters = new Dictionary<string, object> { ["type"] = "object" } },
                                    new ToolDefinition { Name = "read_file", Description = "Read a file", Parameters = new Dictionary<string, object> { ["type"] = "object" } }
                                },
                                ToolChoice = "auto"
                            };
                            for (int round = 0; round < 3; round++)
                            {
                                ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, ct).ConfigureAwait(false);
                                if (!response.Success) throw new Exception(response.Error);
                                string reasoning = String.Empty;
                                await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(ct).ConfigureAwait(false)) reasoning += chunk.ReasoningText;
                                if (String.IsNullOrWhiteSpace(reasoning)) throw new Exception("Reasoning deltas must be separate from answer text.");
                                if (response.Usage?.TotalTokens != 30) throw new Exception("Native usage must survive streaming.");
                                if (round == 2)
                                {
                                    if (response.ToolCalls?.Count > 0 || !(response.Text ?? String.Empty).Contains("Actual file:")) throw new Exception("Expected a final answer after two tool rounds.");
                                    break;
                                }
                                if (response.ToolCalls?.Count != 1) throw new Exception("Expected a tool call.");
                                request.Messages.Add(response.ToAssistantMessage());
                                ToolCall call = response.ToolCalls[0];
                                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, "fixture tool result"));
                            }
                            EmbeddingClientBase embedder = ModelClientFactory.CreateEmbedding(runner, "fixture-key", logging);
                            EmbeddingResponse embedded = await embedder.EmbedAsync(new List<string> { "first", "second" }, token: ct).ConfigureAwait(false);
                            if (!embedded.Success || embedded.Embeddings?.Count != 2) throw new Exception("Expected two native embeddings.");
                            for (int index = 0; index < 2; index++)
                            {
                                EmbeddingResult vector = embedded.Embeddings[index];
                                if (vector.Index != index || vector.Embedding.Length != 3 || vector.Embedding[0] != 1) throw new Exception("Native embedding indices and values must survive parsing.");
                            }
                        }
                    })
            });
        }
    }
}
