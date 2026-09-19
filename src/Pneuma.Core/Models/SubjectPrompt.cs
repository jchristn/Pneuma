namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A per-subject override of a named prompt. When present, it combines with the global default according
    /// to <see cref="MergeMode"/>; when absent, the global default applies (global fallback).
    /// </summary>
    public class SubjectPrompt
    {
        #region Public-Members

        /// <summary>Subject prompt identifier (prefix "sp_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Owning subject identifier.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>Prompt key this override applies to (e.g. "cell.summarize").</summary>
        public string PromptKey
        {
            get { return _PromptKey; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(PromptKey)); _PromptKey = value; }
        }

        /// <summary>Override content.</summary>
        public string? Content { get; set; } = null;

        /// <summary>How the override combines with the global default.</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSubjectPromptId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private string _PromptKey = String.Empty;

        #endregion
    }
}
