namespace Test.Shared.Support
{
    using System;

    /// <summary>Identifiers of a tenant, link, and ingestion job seeded by a test.</summary>
    public class SeededJob
    {
        #region Public-Members

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>Link identifier.</summary>
        public string LinkId { get; set; } = String.Empty;

        /// <summary>Job identifier.</summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>RecallDB collection identifier, when one was created.</summary>
        public string? CollectionId { get; set; } = null;

        #endregion
    }
}
