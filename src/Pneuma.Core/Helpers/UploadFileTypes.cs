namespace Pneuma.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Pneuma.Core.Responses;

    /// <summary>
    /// File-name and content-type rules for the file upload API: normalizing an uploaded file name, inferring a content
    /// type from an extension, and the advisory list of document types clients may pre-check against.
    /// </summary>
    public static class UploadFileTypes
    {
        #region Public-Members

        /// <summary>Longest accepted file name, in characters.</summary>
        public const int MaxFileNameLength = 256;

        /// <summary>The generic binary content type, which means "infer from the extension".</summary>
        public const string OctetStream = "application/octet-stream";

        #endregion

        #region Private-Members

        private static readonly List<UploadSupportedType> _Supported = new List<UploadSupportedType>
        {
            Type("Pdf", new[] { "pdf" }, new[] { "application/pdf" }),
            Type("Docx", new[] { "docx" }, new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }),
            Type("Xlsx", new[] { "xlsx" }, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }),
            Type("Pptx", new[] { "pptx" }, new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" }),
            Type("Csv", new[] { "csv" }, new[] { "text/csv" }),
            Type("Tsv", new[] { "tsv" }, new[] { "text/tab-separated-values" }),
            Type("Json", new[] { "json" }, new[] { "application/json" }),
            Type("Xml", new[] { "xml" }, new[] { "application/xml", "text/xml" }),
            Type("Html", new[] { "html", "htm" }, new[] { "text/html" }),
            Type("Markdown", new[] { "md", "markdown" }, new[] { "text/markdown" }),
            Type("Text", new[] { "txt", "text", "log" }, new[] { "text/plain" }),
            Type("Rtf", new[] { "rtf" }, new[] { "application/rtf", "text/rtf" }),
            Type("Png", new[] { "png" }, new[] { "image/png" }),
            Type("Jpeg", new[] { "jpg", "jpeg" }, new[] { "image/jpeg" }),
            Type("Gif", new[] { "gif" }, new[] { "image/gif" }),
            Type("Bmp", new[] { "bmp" }, new[] { "image/bmp" }),
            Type("Tiff", new[] { "tif", "tiff" }, new[] { "image/tiff" }),
            Type("WebP", new[] { "webp" }, new[] { "image/webp" })
        };

        #endregion

        #region Public-Methods

        /// <summary>The advisory supported types (a fresh copy each call).</summary>
        /// <returns>The types.</returns>
        public static List<UploadSupportedType> Supported()
        {
            return _Supported.Select(t => new UploadSupportedType
            {
                DocumentType = t.DocumentType,
                Extensions = new List<string>(t.Extensions),
                MimeTypes = new List<string>(t.MimeTypes)
            }).ToList();
        }

        /// <summary>
        /// Reduce a submitted file name to its last path segment, trimmed, with control characters removed. Returns null
        /// when nothing usable remains.
        /// </summary>
        /// <param name="fileName">Submitted file name, possibly with a path.</param>
        /// <returns>The normalized name, or null.</returns>
        public static string? NormalizeFileName(string? fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName)) return null;
            string name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            name = new string(name.Where(c => !Char.IsControl(c)).ToArray()).Trim();
            if (name.Length == 0 || name == "." || name == "..") return null;
            return name;
        }

        /// <summary>The lower-case extension of a file name without the dot, or an empty string.</summary>
        /// <param name="fileName">File name.</param>
        /// <returns>The extension.</returns>
        public static string ExtensionOf(string? fileName)
        {
            if (String.IsNullOrEmpty(fileName)) return String.Empty;
            string ext = Path.GetExtension(fileName);
            return String.IsNullOrEmpty(ext) ? String.Empty : ext.TrimStart('.').ToLowerInvariant();
        }

        /// <summary>
        /// The content type to record for an upload: the declared type without parameters, or, when it is missing or
        /// generic (<c>application/octet-stream</c>), the type the extension implies, else <c>application/octet-stream</c>.
        /// </summary>
        /// <param name="declared">Declared content type, possibly null.</param>
        /// <param name="fileName">File name.</param>
        /// <returns>The content type, lower case.</returns>
        public static string ResolveContentType(string? declared, string? fileName)
        {
            string ct = (declared ?? String.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (ct.Length > 0 && ct != OctetStream) return ct;
            string? inferred = ContentTypeForExtension(ExtensionOf(fileName));
            return inferred ?? OctetStream;
        }

        /// <summary>The usual content type for an extension, or null when unknown.</summary>
        /// <param name="extension">Extension without the dot.</param>
        /// <returns>The content type, or null.</returns>
        public static string? ContentTypeForExtension(string? extension)
        {
            if (String.IsNullOrEmpty(extension)) return null;
            string ext = extension.TrimStart('.').ToLowerInvariant();
            foreach (UploadSupportedType type in _Supported)
            {
                if (type.Extensions.Contains(ext)) return type.MimeTypes[0];
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static UploadSupportedType Type(string documentType, string[] extensions, string[] mimeTypes)
        {
            return new UploadSupportedType
            {
                DocumentType = documentType,
                Extensions = new List<string>(extensions),
                MimeTypes = new List<string>(mimeTypes)
            };
        }

        #endregion
    }
}
