namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A named, versioned prompt used for ingestion classification/summarization or user-query answering.
    /// </summary>
    public class Prompt
    {
        #region Public-Members

        /// <summary>Prompt identifier (prefix "prm_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier. Null for global prompts.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Stable key identifying the prompt's role (e.g. "ontology.classify", "user.answer").</summary>
        public string Key
        {
            get { return _Key; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Key)); _Key = value; }
        }

        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>The prompt content / template.</summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>Monotonic version number.</summary>
        public int Version
        {
            get { return _Version; }
            set { _Version = value < 1 ? 1 : value; }
        }

        /// <summary>Whether the prompt is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the prompt is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GeneratePromptId();
        private string _Key = String.Empty;
        private int _Version = 1;

        #endregion
    }
}
