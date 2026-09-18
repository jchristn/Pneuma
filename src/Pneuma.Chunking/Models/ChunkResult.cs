namespace Pneuma.Chunking.Models
{
    /// <summary>
    /// Result of a single chunk with its text and computed embeddings.
    /// </summary>
    public class ChunkResult
    {
        private Guid _CellGUID = Guid.Empty;
        private string _Text = string.Empty;
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private List<float> _Embeddings = new List<float>();

        /// <summary>
        /// The GUID of the SemanticCellRequest that produced this chunk.
        /// </summary>
        public Guid CellGUID
        {
            get => _CellGUID;
            set => _CellGUID = value;
        }

        /// <summary>
        /// Original text of this chunk.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? string.Empty;
        }

        /// <summary>
        /// Labels echoed from the request.
        /// </summary>
        public List<string> Labels
        {
            get => _Labels;
            set => _Labels = value ?? new List<string>();
        }

        /// <summary>
        /// Tags echoed from the request.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Computed embedding vector.
        /// </summary>
        public List<float> Embeddings
        {
            get => _Embeddings;
            set => _Embeddings = value ?? new List<float>();
        }
    }
}
