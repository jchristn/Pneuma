namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Chunking.Chunking;
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;
    using Touchstone.Core;

    /// <summary>
    /// Self-consistency tests for the ported Pneuma.Chunking library (chunking + tokenization).
    /// These are NOT full golden-file parity tests; they verify internal invariants only.
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
                displayName: "Chunking and Tokenization",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Chunking", "FixedTokenCount_RespectsTokenLimit", "FixedTokenCount chunking of a multi-paragraph sample yields chunks each within the token limit",
                        executeAsync: ct =>
                        {
                            ITokenizerAdapter tokenizer = new SharpTokenTokenizerAdapter("cl100k_base");
                            int tokenLimit = 32;
                            ChunkingConfiguration config = new ChunkingConfiguration();
                            config.Strategy = Pneuma.Chunking.Enums.ChunkStrategyEnum.FixedTokenCount;

                            List<string> chunks = FixedTokenChunker.Chunk(_MultiParagraphSample, config, tokenizer, tokenLimit);
                            if (chunks.Count == 0) throw new Exception("expected at least one chunk");
                            foreach (string chunk in chunks)
                            {
                                int count = tokenizer.CountTokens(chunk);
                                if (count <= 0) throw new Exception("chunk had zero tokens");
                                if (count > tokenLimit) throw new Exception("chunk exceeded token limit: " + count + " > " + tokenLimit);
                            }

                            return System.Threading.Tasks.Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Chunking", "Chunking_IsDeterministic", "Chunking the same input twice yields identical output",
                        executeAsync: ct =>
                        {
                            ITokenizerAdapter tokenizer = new SharpTokenTokenizerAdapter("cl100k_base");
                            int tokenLimit = 40;
                            ChunkingConfiguration config = new ChunkingConfiguration();

                            List<string> first = FixedTokenChunker.Chunk(_MultiParagraphSample, config, tokenizer, tokenLimit);
                            List<string> second = FixedTokenChunker.Chunk(_MultiParagraphSample, config, tokenizer, tokenLimit);

                            if (first.Count != second.Count) throw new Exception("chunk count differed between runs");
                            for (int i = 0; i < first.Count; i++)
                            {
                                if (!String.Equals(first[i], second[i], StringComparison.Ordinal))
                                    throw new Exception("chunk " + i + " differed between runs");
                            }

                            return System.Threading.Tasks.Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Chunking", "SentenceAndParagraph_ReconstructAllWords", "SentenceBased and ParagraphBased produce non-empty chunks that reconstruct all source words",
                        executeAsync: ct =>
                        {
                            ITokenizerAdapter tokenizer = new SharpTokenTokenizerAdapter("cl100k_base");
                            int tokenLimit = 64;
                            ChunkingConfiguration config = new ChunkingConfiguration();

                            List<string> sourceWords = SplitWords(_MultiParagraphSample);

                            List<string> sentenceChunks = SentenceChunker.Chunk(_MultiParagraphSample, config, tokenizer, tokenLimit);
                            AssertReconstructsWords(sentenceChunks, sourceWords, "SentenceBased");

                            List<string> paragraphChunks = ParagraphChunker.Chunk(_MultiParagraphSample, config, tokenizer, tokenLimit);
                            AssertReconstructsWords(paragraphChunks, sourceWords, "ParagraphBased");

                            return System.Threading.Tasks.Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Chunking", "SharpTokenAdapter_RoundTrips", "The SharpToken adapter round-trips Encode/Decode and CountTokens matches Encode().Count",
                        executeAsync: ct =>
                        {
                            ITokenizerAdapter tokenizer = new SharpTokenTokenizerAdapter("cl100k_base");
                            string text = "Round-trip verification of the cl100k_base tokenizer, with symbols: 15.2% and 1 - 3 days.";

                            IReadOnlyList<int> encoded = tokenizer.Encode(text);
                            if (encoded.Count == 0) throw new Exception("expected non-empty encoding");
                            if (tokenizer.CountTokens(text) != encoded.Count) throw new Exception("CountTokens did not match Encode().Count");

                            string decoded = tokenizer.Decode(encoded);
                            if (!String.Equals(decoded, text, StringComparison.Ordinal)) throw new Exception("Encode/Decode did not round-trip");

                            return System.Threading.Tasks.Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Chunking", "BertAdapter_LoadsVocabAndTokenizes", "The BERT adapter loads its embedded vocab and tokenizes a sample without error",
                        executeAsync: ct =>
                        {
                            ITokenizerAdapter tokenizer = new BertWordPieceTokenizerAdapter();
                            string text = "BERT WordPiece tokenization of a simple sample sentence.";

                            int count = tokenizer.CountTokens(text);
                            if (count <= 0) throw new Exception("expected a positive token count from the BERT adapter");

                            IReadOnlyList<int> encoded = tokenizer.Encode(text);
                            if (encoded.Count == 0) throw new Exception("expected non-empty BERT encoding");

                            string slice = tokenizer.SliceByTokenRange(text, 0, Math.Min(4, count));
                            if (String.IsNullOrEmpty(slice)) throw new Exception("expected a non-empty token slice from the BERT adapter");

                            return System.Threading.Tasks.Task.CompletedTask;
                        })
                });
        }

        private static List<string> SplitWords(string text)
        {
            return text
                .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static void AssertReconstructsWords(List<string> chunks, List<string> sourceWords, string strategyName)
        {
            if (chunks.Count == 0) throw new Exception(strategyName + " produced no chunks");
            foreach (string chunk in chunks)
            {
                if (String.IsNullOrWhiteSpace(chunk)) throw new Exception(strategyName + " produced an empty chunk");
            }

            List<string> chunkWords = SplitWords(String.Join(" ", chunks));
            HashSet<string> chunkWordSet = new HashSet<string>(chunkWords, StringComparer.Ordinal);
            foreach (string word in sourceWords)
            {
                if (!chunkWordSet.Contains(word))
                    throw new Exception(strategyName + " chunks did not reconstruct source word: '" + word + "'");
            }
        }
    }
}
