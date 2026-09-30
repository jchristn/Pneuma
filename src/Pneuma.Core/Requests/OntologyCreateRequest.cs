namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to create a tenant ontology. Its first version is a draft: empty, built from a template, or copied from an existing version.</summary>
    public class OntologyCreateRequest
    {
        #region Public-Members

        /// <summary>Ontology name, unique within the tenant. Required; at most 256 characters.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Optional description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Built-in template to start from (see <c>GET /v1.0/ontology-templates</c>), or null.</summary>
        public string? Template { get; set; } = null;

        /// <summary>An existing version (of any ontology in the tenant) to copy, or null. Ignored when a template is given.</summary>
        public string? CopyFromVersionId { get; set; } = null;

        #endregion
    }
}
