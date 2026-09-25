namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Security;
    using SyslogLogging;
    using Test.Shared.Support;
    using TextChunker.Tokenization;
    using Touchstone.Core;

    /// <summary>
    /// Chunking through Pneuma's seam (<see cref="NativeSemanticProcessor.ChunkAsync"/>, backed by the TextChunker
    /// library): token limits, determinism, word preservation, no redundant tail chunks under overlap, surrogate-safe
    /// boundaries, and model-aware token counting.
    /// </summary>
    public static class ChunkingSuite
    {
        private const string _MultiParagraphSample =
            "The quick brown fox jumps over the lazy dog. The dog was not amused by the fox. "
            + "It had been sleeping soundly for hours before the interruption occurred.\n\n"
            + "Meanwhile, a second paragraph describes an entirely different scene. Rain fell on the quiet town. "
            + "Streets emptied as people retreated indoors to wait out the storm.\n\n"
            + "A final paragraph closes the sample. Chunking must preserve every meaningful word. "
            + "Determinism matters more than anything else in this pipeline.";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Chunking",
                displayName: "Chunking (TextChunker behind NativeSemanticProcessor)",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Chunking", "FixedTokenCount_RespectsTokenLimit", "FixedTokenCount chunking yields chunks within the token limit (cl100k_base when no model is named)",
                        executeAsync: async ct =>
                        {
                            List<SemanticChunk> chunks = await ChunkAsync(_MultiParagraphSample, new ChunkingOptions { Strategy = "FixedTokenCount", MaxTokens = 32, OverlapCount = 0 }, ct);
                            if (chunks.Count < 2) throw new Exception("expected the sample to span several chunks, got " + chunks.Count);
                            SharpTokenTokenizerAdapter tokenizer = new SharpTokenTokenizerAdapter("cl100k_base");
                            foreach (SemanticChunk chunk in chunks)
                            {
                                int count = tokenizer.CountTokens(chunk.Text);
                                if (count <= 0 || count > 32) throw new Exception("chunk had " + count + " tokens; limit 32");
                            }
                        }),

                    new TestCaseDescriptor("Chunking", "Chunking_IsDeterministic", "Chunking the same input twice yields identical output",
                        executeAsync: async ct =>
                        {
                            ChunkingOptions options = new ChunkingOptions { Strategy = "FixedTokenCount", MaxTokens = 40, OverlapCount = 8 };
                            List<SemanticChunk> first = await ChunkAsync(_MultiParagraphSample, options, ct);
                            List<SemanticChunk> second = await ChunkAsync(_MultiParagraphSample, options, ct);
                            if (!first.Select(c => c.Text).SequenceEqual(second.Select(c => c.Text))) throw new Exception("chunking is not deterministic");
                        }),

                    new TestCaseDescriptor("Chunking", "Strategies_ReconstructAllWords", "SentenceBased, ParagraphBased, and Recursive keep every source word",
                        executeAsync: async ct =>
                        {
                            HashSet<string> sourceWords = Words(_MultiParagraphSample);
                            foreach (string strategy in new[] { "SentenceBased", "ParagraphBased", "Recursive" })
                            {
                                List<SemanticChunk> chunks = await ChunkAsync(_MultiParagraphSample, new ChunkingOptions { Strategy = strategy, MaxTokens = 40, OverlapCount = 0 }, ct);
                                if (chunks.Count == 0 || chunks.Any(c => String.IsNullOrWhiteSpace(c.Text))) throw new Exception(strategy + " produced empty output");
                                HashSet<string> chunkWords = Words(String.Join(" ", chunks.Select(c => c.Text)));
                                List<string> missing = sourceWords.Where(w => !chunkWords.Contains(w)).ToList();
                                if (missing.Count > 0) throw new Exception(strategy + " dropped words: " + String.Join(", ", missing));
                            }
                        }),

                    new TestCaseDescriptor("Chunking", "Overlap_NoRedundantTailChunks", "Fixed-token chunking with overlap never emits a chunk wholly contained in the one before it",
                        executeAsync: async ct =>
                        {
                            string text = String.Join(" ", Enumerable.Range(1, 400).Select(i => "word" + i));
                            List<SemanticChunk> chunks = await ChunkAsync(text, new ChunkingOptions { Strategy = "FixedTokenCount", MaxTokens = 64, OverlapCount = 32 }, ct);
                            for (int i = 1; i < chunks.Count; i++)
                            {
                                if (chunks[i - 1].Text.Contains(chunks[i].Text, StringComparison.Ordinal)) throw new Exception("chunk " + i + " repeats the end of chunk " + (i - 1) + ": '" + chunks[i].Text + "'");
                            }
                            if (!chunks[chunks.Count - 1].Text.Contains("word400", StringComparison.Ordinal)) throw new Exception("the last word must be in the last chunk");
                        }),

                    new TestCaseDescriptor("Chunking", "Emoji_SurrogatePairsStayWhole", "Chunk boundaries never split a surrogate pair: no U+FFFD, no lone surrogate, every emoji kept",
                        executeAsync: async ct =>
                        {
                            string text = String.Concat(Enumerable.Range(0, 120).Select(i => "Status " + i + " \U0001F680\U0001F389 ok. "));
                            foreach (string model in new[] { null, "all-minilm" })
                            {
                                List<SemanticChunk> chunks = await ChunkAsync(text, new ChunkingOptions { Strategy = "FixedTokenCount", MaxTokens = 16, OverlapCount = 0, ModelId = model }, ct);
                                string joined = String.Concat(chunks.Select(c => c.Text));
                                if (joined.Contains('�')) throw new Exception("chunks contain U+FFFD (model " + (model ?? "cl100k") + ")");
                                for (int i = 0; i < joined.Length; i++)
                                {
                                    bool high = Char.IsHighSurrogate(joined[i]);
                                    if (high && (i + 1 >= joined.Length || !Char.IsLowSurrogate(joined[i + 1]))) throw new Exception("lone high surrogate at " + i);
                                    if (Char.IsLowSurrogate(joined[i]) && (i == 0 || !Char.IsHighSurrogate(joined[i - 1]))) throw new Exception("lone low surrogate at " + i);
                                }
                                int rockets = CountOf(joined, "\U0001F680");
                                if (rockets < 120) throw new Exception("emoji lost: " + rockets + " of 120 (model " + (model ?? "cl100k") + ")");
                            }
                        }),

                    new TestCaseDescriptor("Chunking", "ModelId_CountsInModelTokens", "Naming a BERT-family embedding model sizes chunks in its WordPiece tokens",
                        executeAsync: async ct =>
                        {
                            List<SemanticChunk> chunks = await ChunkAsync(_MultiParagraphSample, new ChunkingOptions { Strategy = "FixedTokenCount", MaxTokens = 24, OverlapCount = 0, ModelId = "nomic-embed-text" }, ct);
                            if (chunks.Count < 2) throw new Exception("expected several chunks");
                            BertWordPieceTokenizerAdapter wordPiece = new BertWordPieceTokenizerAdapter();
                            foreach (SemanticChunk chunk in chunks)
                            {
                                int count = wordPiece.CountTokens(chunk.Text);
                                if (count > 24) throw new Exception("chunk has " + count + " WordPiece tokens; limit 24");
                            }
                        })
                });
        }

        private static async Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken ct)
        {
            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                NativeSemanticProcessor processor = new NativeSemanticProcessor(db, new Aes256Cipher("test-signing-key"), logging);
                return await processor.ChunkAsync(text, options, ct);
            }
        }

        private static HashSet<string> Words(string text)
        {
            HashSet<string> words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = new string(raw.Where(Char.IsLetterOrDigit).ToArray());
                if (word.Length > 0) words.Add(word);
            }

            return words;
        }

        private static int CountOf(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
