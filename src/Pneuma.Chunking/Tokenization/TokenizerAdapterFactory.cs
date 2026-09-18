namespace Pneuma.Chunking.Tokenization
{
    using Pneuma.Chunking.Enums;
    using Pneuma.Chunking.Models;

    /// <summary>
    /// Creates tokenizer adapters for resolved profiles.
    /// </summary>
    public static class TokenizerAdapterFactory
    {
        /// <summary>
        /// Create a tokenizer adapter for the supplied resolved profile.
        /// </summary>
        public static ITokenizerAdapter Create(ResolvedTokenizationProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            switch (profile.TokenizerKind)
            {
                case TokenizerKindEnum.Cl100kBase:
                    return new SharpTokenTokenizerAdapter(string.IsNullOrWhiteSpace(profile.TokenizerModel) ? "cl100k_base" : profile.TokenizerModel);
                case TokenizerKindEnum.BertWordPiece:
                    return new BertWordPieceTokenizerAdapter();
                default:
                    throw new ArgumentException("Unsupported tokenizer kind: " + profile.TokenizerKind, nameof(profile));
            }
        }
    }
}
