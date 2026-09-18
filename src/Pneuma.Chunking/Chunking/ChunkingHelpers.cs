namespace Pneuma.Chunking.Chunking
{
    using System.Text.RegularExpressions;
    using Pneuma.Chunking.Enums;
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Shared helpers for token-budget-aware chunking strategies.
    /// </summary>
    internal static class ChunkingHelpers
    {
        private static readonly Regex _SentenceBoundaryRegex = new Regex(@"[.!?][\s]", RegexOptions.Compiled | RegexOptions.RightToLeft);

        public static List<string> ChunkByTokenSpans(
            string text,
            ChunkingConfiguration config,
            ITokenizerAdapter tokenizer,
            int tokenLimit)
        {
            if (string.IsNullOrEmpty(text) || tokenLimit <= 0) return new List<string>();

            int totalTokens = tokenizer.CountTokens(text);
            if (totalTokens <= 0) return new List<string>();

            int overlapTokens = GetOverlapTokenCount(tokenLimit, config);
            List<string> chunks = new List<string>();
            int position = 0;

            while (position < totalTokens)
            {
                int requestedTokenCount = Math.Min(tokenLimit, totalTokens - position);
                TokenSlice slice = CreateStrictTokenSlice(text, position, requestedTokenCount, tokenizer, tokenLimit);
                if (slice.TokenCount <= 0) break;

                if (!string.IsNullOrWhiteSpace(slice.Text))
                    chunks.Add(slice.Text);

                if (position + slice.TokenCount >= totalTokens) break;

                int advance = slice.TokenCount - overlapTokens;
                if (advance <= 0) advance = 1;

                if (config.OverlapStrategy == OverlapStrategyEnum.SentenceBoundaryAware && overlapTokens > 0)
                {
                    int adjusted = AdjustToSentenceBoundary(tokenizer, text, Math.Min(position + advance, totalTokens));
                    position = adjusted > position ? adjusted : position + advance;
                }
                else if (config.OverlapStrategy == OverlapStrategyEnum.SemanticBoundaryAware && overlapTokens > 0)
                {
                    int adjusted = AdjustToParagraphBoundary(tokenizer, text, Math.Min(position + advance, totalTokens));
                    position = adjusted > position ? adjusted : position + advance;
                }
                else
                {
                    position += advance;
                }
            }

            return chunks;
        }

        private static TokenSlice CreateStrictTokenSlice(
            string text,
            int startTokenIndex,
            int requestedTokenCount,
            ITokenizerAdapter tokenizer,
            int tokenLimit)
        {
            if (requestedTokenCount <= 0) return new TokenSlice(string.Empty, 0);

            int candidateTokenCount = requestedTokenCount;
            while (candidateTokenCount > 0)
            {
                string chunkText = tokenizer.SliceByTokenRange(text, startTokenIndex, candidateTokenCount);
                if (string.IsNullOrWhiteSpace(chunkText))
                {
                    candidateTokenCount--;
                    continue;
                }

                int actualTokenCount = tokenizer.CountTokens(chunkText);
                if (actualTokenCount > 0 && actualTokenCount <= tokenLimit)
                    return new TokenSlice(chunkText, actualTokenCount);

                candidateTokenCount = actualTokenCount > 0
                    ? Math.Min(candidateTokenCount - 1, actualTokenCount - 1)
                    : candidateTokenCount - 1;
            }

            return new TokenSlice(string.Empty, 0);
        }

        public static List<string> ChunkUnits(
            IReadOnlyList<string> units,
            string separator,
            int tokenLimit,
            ITokenizerAdapter tokenizer,
            int overlapUnits,
            Func<string, List<string>> oversizedUnitHandler)
        {
            if (units == null || units.Count == 0 || tokenLimit <= 0) return new List<string>();
            if (tokenizer == null) throw new ArgumentNullException(nameof(tokenizer));
            if (oversizedUnitHandler == null) throw new ArgumentNullException(nameof(oversizedUnitHandler));

            List<string> filtered = units.Where(unit => !string.IsNullOrWhiteSpace(unit)).ToList();
            if (filtered.Count == 0) return new List<string>();

            List<string> chunks = new List<string>();
            int index = 0;

            while (index < filtered.Count)
            {
                int startIndex = index;
                List<string> currentUnits = new List<string>();

                while (index < filtered.Count)
                {
                    string unit = filtered[index].Trim();

                    if (tokenizer.CountTokens(unit) > tokenLimit)
                    {
                        if (currentUnits.Count > 0) break;

                        List<string> oversizedChunks = oversizedUnitHandler(unit);
                        oversizedChunks = NormalizeOversizedChunks(unit, oversizedChunks, tokenLimit, tokenizer);
                        chunks.AddRange(oversizedChunks);
                        index++;
                        startIndex = index;
                        continue;
                    }

                    string candidate = currentUnits.Count == 0
                        ? unit
                        : string.Join(separator, currentUnits.Concat(new[] { unit }));

                    if (currentUnits.Count > 0 && tokenizer.CountTokens(candidate) > tokenLimit)
                        break;

                    currentUnits.Add(unit);
                    index++;
                }

                if (currentUnits.Count > 0)
                {
                    chunks.Add(string.Join(separator, currentUnits));
                    if (overlapUnits > 0 && index < filtered.Count)
                    {
                        int rewind = Math.Min(overlapUnits, Math.Max(0, currentUnits.Count - 1));
                        index = Math.Max(startIndex + 1, index - rewind);
                    }
                }
            }

            return chunks;
        }

        public static List<string> SplitParagraphs(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            return text
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => paragraph.Trim())
                .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
                .ToList();
        }

        public static List<string> SplitSentences(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            return SentenceChunker.SentencePattern
                .Split(text)
                .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
                .Select(sentence => sentence.Trim())
                .ToList();
        }

        public static int GetUnitOverlapCount(ChunkingConfiguration config)
        {
            if (config.OverlapPercentage.HasValue)
                return config.OverlapPercentage.Value > 0 ? 1 : 0;

            return Math.Max(0, config.OverlapCount);
        }

        private static List<string> NormalizeOversizedChunks(
            string originalUnit,
            List<string> candidateChunks,
            int tokenLimit,
            ITokenizerAdapter tokenizer)
        {
            if (candidateChunks == null || candidateChunks.Count == 0)
                return ChunkByTokenSpans(originalUnit, new ChunkingConfiguration(), tokenizer, tokenLimit);

            List<string> normalized = new List<string>();
            foreach (string candidate in candidateChunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk)))
            {
                if (tokenizer.CountTokens(candidate) <= tokenLimit)
                {
                    normalized.Add(candidate);
                }
                else
                {
                    normalized.AddRange(ChunkByTokenSpans(candidate, new ChunkingConfiguration(), tokenizer, tokenLimit));
                }
            }

            if (normalized.Count == 0)
                normalized.AddRange(ChunkByTokenSpans(originalUnit, new ChunkingConfiguration(), tokenizer, tokenLimit));

            return normalized;
        }

        private static int GetOverlapTokenCount(int chunkSize, ChunkingConfiguration config)
        {
            if (config.OverlapPercentage.HasValue)
                return (int)(chunkSize * config.OverlapPercentage.Value);

            return config.OverlapCount;
        }

        private static int AdjustToSentenceBoundary(ITokenizerAdapter tokenizer, string text, int tokenPosition)
        {
            string decodedUpToPos = tokenizer.SliceByTokenRange(text, 0, tokenPosition);
            Match match = _SentenceBoundaryRegex.Match(decodedUpToPos);
            if (match.Success)
            {
                string upToSentence = decodedUpToPos.Substring(0, match.Index + 1);
                return tokenizer.CountTokens(upToSentence);
            }

            return tokenPosition;
        }

        private static int AdjustToParagraphBoundary(ITokenizerAdapter tokenizer, string text, int tokenPosition)
        {
            string decodedUpToPos = tokenizer.SliceByTokenRange(text, 0, tokenPosition);
            int lastParagraphIndex = decodedUpToPos.LastIndexOf("\n\n", StringComparison.Ordinal);
            if (lastParagraphIndex > 0)
            {
                string upToParagraph = decodedUpToPos.Substring(0, lastParagraphIndex + 2);
                return tokenizer.CountTokens(upToParagraph);
            }

            return tokenPosition;
        }

        private readonly record struct TokenSlice(string Text, int TokenCount);
    }
}
