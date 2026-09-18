namespace Pneuma.Chunking.Tokenization
{
    using System.Reflection;
    using Microsoft.ML.Tokenizers;

    /// <summary>
    /// Tokenizer adapter backed by a local BERT WordPiece vocabulary.
    /// </summary>
    public class BertWordPieceTokenizerAdapter : ITokenizerAdapter
    {
        private static readonly Lazy<Tokenizer> _Tokenizer = new Lazy<Tokenizer>(CreateTokenizer, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return _Tokenizer.Value.CountTokens(text);
        }

        /// <inheritdoc />
        public IReadOnlyList<int> Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();
            return _Tokenizer.Value.EncodeToIds(text, considerPreTokenization: true, considerNormalization: true).ToArray();
        }

        /// <inheritdoc />
        public string Decode(IEnumerable<int> tokenIds)
        {
            if (tokenIds == null) throw new ArgumentNullException(nameof(tokenIds));
            return _Tokenizer.Value.Decode(tokenIds.ToArray());
        }

        /// <inheritdoc />
        public string SliceByTokenRange(string text, int startTokenIndex, int tokenCount)
        {
            if (string.IsNullOrEmpty(text) || tokenCount <= 0) return string.Empty;
            if (startTokenIndex < 0) throw new ArgumentOutOfRangeException(nameof(startTokenIndex));

            int startCharIndex = 0;
            if (startTokenIndex > 0)
            {
                startCharIndex = _Tokenizer.Value.GetIndexByTokenCount(
                    text.AsSpan(),
                    startTokenIndex,
                    out _,
                    out _);
            }

            if (startCharIndex >= text.Length) return string.Empty;

            ReadOnlySpan<char> remaining = text.AsSpan(startCharIndex);
            int lengthCharIndex = _Tokenizer.Value.GetIndexByTokenCount(
                remaining,
                tokenCount,
                out _,
                out _);

            if (lengthCharIndex <= 0)
            {
                return remaining.ToString();
            }

            return remaining.Slice(0, Math.Min(lengthCharIndex, remaining.Length)).ToString();
        }

        private static Tokenizer CreateTokenizer()
        {
            Assembly assembly = typeof(BertWordPieceTokenizerAdapter).Assembly;
            const string resourceName = "Pneuma.Chunking.Tokenization.Data.bert-base-uncased-vocab.txt";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new InvalidOperationException("Unable to load embedded tokenizer vocabulary: " + resourceName);

            return BertTokenizer.Create(
                stream,
                new BertOptions
                {
                    LowerCaseBeforeTokenization = true,
                    ApplyBasicTokenization = true
                });
        }
    }
}
