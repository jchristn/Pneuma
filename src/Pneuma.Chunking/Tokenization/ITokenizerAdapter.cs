namespace Pneuma.Chunking.Tokenization
{
    /// <summary>
    /// Provider-agnostic tokenizer operations used by chunkers and runtime services.
    /// </summary>
    public interface ITokenizerAdapter
    {
        /// <summary>
        /// Count tokens for the supplied text.
        /// </summary>
        int CountTokens(string text);

        /// <summary>
        /// Encode text into tokenizer-native token identifiers.
        /// </summary>
        IReadOnlyList<int> Encode(string text);

        /// <summary>
        /// Decode tokenizer-native token identifiers back into text.
        /// </summary>
        string Decode(IEnumerable<int> tokenIds);

        /// <summary>
        /// Slice a text span by tokenizer-native token range.
        /// </summary>
        string SliceByTokenRange(string text, int startTokenIndex, int tokenCount);
    }
}
