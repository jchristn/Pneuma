namespace Pneuma.Chunking.Enums
{
    /// <summary>
    /// Supported tokenizer adapter families.
    /// </summary>
    public enum TokenizerKindEnum
    {
        /// <summary>OpenAI-style cl100k_base BPE tokenization.</summary>
        Cl100kBase,
        /// <summary>BERT-style WordPiece tokenization.</summary>
        BertWordPiece
    }
}
