namespace Pneuma.Core.Helpers
{
    using System;

    /// <summary>Blob-store keys for content pushed through the API (inline links).</summary>
    public static class InlineContentKeys
    {
        #region Public-Methods

        /// <summary>The blob key holding an inline link's content.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="linkId">Link identifier.</param>
        /// <returns>The key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        public static string KeyFor(string tenantId, string linkId)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(linkId)) throw new ArgumentNullException(nameof(linkId));
            return "inline/" + tenantId + "/" + linkId;
        }

        /// <summary>The URI an inline link carries in place of a URL.</summary>
        /// <param name="linkId">Link identifier.</param>
        /// <returns>The URI.</returns>
        public static string UriFor(string linkId)
        {
            return "pneuma-inline://" + linkId;
        }

        /// <summary>Map a declared content type to the DocumentAtom document type used to extract it, or null when unsupported.</summary>
        /// <param name="contentType">Content type, for example <c>text/markdown</c>.</param>
        /// <returns>The document type, or null.</returns>
        public static string? DocumentTypeFor(string? contentType)
        {
            string ct = (contentType ?? String.Empty).Split(';')[0].Trim().ToLowerInvariant();
            switch (ct)
            {
                case "text/plain": return "Text";
                case "text/markdown": return "Markdown";
                case "text/html": return "Html";
                case "application/json": return "Json";
                default: return null;
            }
        }

        #endregion
    }
}
