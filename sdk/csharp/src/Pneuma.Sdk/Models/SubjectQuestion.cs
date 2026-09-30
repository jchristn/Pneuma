namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>A subject's starter question.</summary>
    public class SubjectQuestion
    {
        /// <summary>Question identifier (sq_).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>The question.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>Kind of question.</summary>
        public SubjectQuestionKindEnum Kind { get; set; } = SubjectQuestionKindEnum.Fact;

        /// <summary>Display order.</summary>
        public int Position { get; set; } = 0;

        /// <summary>Who wrote it.</summary>
        public SubjectQuestionOriginEnum Origin { get; set; } = SubjectQuestionOriginEnum.User;

        /// <summary>Creation time (UTC).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
