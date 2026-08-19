namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A subject archive owned by a tenant. Its knowledge graph is rooted at a LiteGraph node.
    /// </summary>
    public class Subject
    {
        #region Public-Members

        /// <summary>Subject identifier (prefix "sub_").</summary>
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

        /// <summary>Display name of the subject.</summary>
        public string DisplayName
        {
            get { return _DisplayName; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DisplayName)); _DisplayName = value; }
        }

        /// <summary>Free-form kind of subject (e.g. "Person"); any value the operator chooses.</summary>
        public string Type { get; set; } = "Person";

        /// <summary>Optional biography / description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Identifier of the root LiteGraph node representing this subject.</summary>
        public string? GraphRootNodeId { get; set; } = null;

        /// <summary>Whether the subject archive is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the subject is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSubjectId();
        private string _TenantId = String.Empty;
        private string _DisplayName = String.Empty;

        #endregion
    }
}
