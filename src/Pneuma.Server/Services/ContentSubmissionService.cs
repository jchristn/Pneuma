namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Storage;

    /// <summary>
    /// Stores and queues content pushed to a subject and files uploaded to it (the REST content and file routes and the
    /// MCP <c>pneuma_submit_content</c> tool share it). Content is kept in the blob store and ingested like a link's; an external key already used in
    /// the subject replaces that link's content instead of adding a duplicate. Thread-safe.
    /// </summary>
    public class ContentSubmissionService
    {
        #region Public-Members

        /// <summary>Items one batch may hold. Default 100.</summary>
        public int MaxBatchItems { get; } = 100;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IBlobStore _Blobs;
        private readonly IngestionSettings _Settings;
        private readonly IAtomizer? _Atomizer;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="blobs">Blob store that holds pushed content.</param>
        /// <param name="settings">Ingestion settings (content and upload size limits).</param>
        /// <param name="atomizer">
        /// Optional type detector used to refuse an uploaded file DocumentAtom does not recognize before it is stored; null
        /// leaves detection to the ingestion job.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ContentSubmissionService(DatabaseDriverBase db, IBlobStore blobs, IngestionSettings settings, IAtomizer? atomizer = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Atomizer = atomizer;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate, store, and queue one item. Returns a result carrying the status the item would have on its own
        /// (201 created, 200 replaced, 400, 409, or 413) and, when rejected, the reason.
        /// </summary>
        /// <param name="userId">The submitting user, or null.</param>
        /// <param name="subject">The target subject (already checked for models and a collection).</param>
        /// <param name="item">The item.</param>
        /// <param name="index">The item's position in its request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subject"/> or <paramref name="item"/> is null.</exception>
        public async Task<ContentSubmitResult> SubmitAsync(string? userId, Subject subject, SubmitContentRequest item, int index, CancellationToken token)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (item == null) throw new ArgumentNullException(nameof(item));

            ContentSubmitResult result = new ContentSubmitResult { Index = index };
            string? content = TextSanitizer.Clean(item.Content);
            if (String.IsNullOrWhiteSpace(content)) return Reject(result, 400, "Content is required.");

            string? documentType = InlineContentKeys.DocumentTypeFor(item.ContentType);
            if (documentType == null) return Reject(result, 400, "contentType must be text/plain, text/markdown, text/html, or application/json; got '" + (item.ContentType ?? String.Empty) + "'.");

            byte[] bytes = Encoding.UTF8.GetBytes(content!);
            if (bytes.Length > _Settings.MaxInlineContentBytes) return Reject(result, 413, "Content is " + bytes.Length + " bytes; the limit is " + _Settings.MaxInlineContentBytes + " (Ingestion.MaxInlineContentBytes).");

            string? externalKey = String.IsNullOrWhiteSpace(item.ExternalKey) ? null : item.ExternalKey!.Trim();
            if (externalKey != null && externalKey.Length > 256) return Reject(result, 400, "externalKey is at most 256 characters.");

            string contentType = item.ContentType!.Split(';')[0].Trim().ToLowerInvariant();
            return await StoreAsync(result, userId, subject, bytes, contentType, null, TextSanitizer.Clean(item.Title), externalKey, item.Labels, item.Tags, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate, store, and queue one uploaded file. The bytes are stored unchanged. A text-family file (plain text,
        /// Markdown, HTML, JSON) keeps its declared type; any other file is checked with the type detector when one is
        /// configured and its type is detected again by the ingestion job. Returns 201 created, 200 replaced, 400, 409,
        /// 413, or 415 with the reason.
        /// </summary>
        /// <param name="userId">The submitting user, or null.</param>
        /// <param name="subject">The target subject (already checked for models and a collection).</param>
        /// <param name="request">File metadata.</param>
        /// <param name="data">The file bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subject"/> or <paramref name="request"/> is null.</exception>
        public async Task<ContentSubmitResult> SubmitFileAsync(string? userId, Subject subject, SubmitFileRequest request, byte[]? data, CancellationToken token)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (request == null) throw new ArgumentNullException(nameof(request));

            ContentSubmitResult result = new ContentSubmitResult { Index = 0 };
            string? fileName = UploadFileTypes.NormalizeFileName(request.FileName);
            if (fileName == null) return Reject(result, 400, "A file name is required (the filename query parameter).");
            if (fileName.Length > UploadFileTypes.MaxFileNameLength) return Reject(result, 400, "The file name is at most " + UploadFileTypes.MaxFileNameLength + " characters.");
            if (data == null || data.Length == 0) return Reject(result, 400, "The file is empty.");
            if (data.LongLength > _Settings.MaxUploadBytes) return Reject(result, 413, TooLargeMessage(data.LongLength));

            string? externalKey = String.IsNullOrWhiteSpace(request.ExternalKey) ? null : request.ExternalKey!.Trim();
            if (externalKey != null && externalKey.Length > 256) return Reject(result, 400, "externalKey is at most 256 characters.");

            string contentType = UploadFileTypes.ResolveContentType(request.ContentType, fileName);
            if (InlineContentKeys.DocumentTypeFor(contentType) == null && _Atomizer != null)
            {
                TypeDetectResult? detected = null;
                try
                {
                    detected = await _Atomizer.DetectTypeAsync(data, token).ConfigureAwait(false);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // Detection at upload is a gate, not a dependency: when the detector is unreachable the file is
                    // accepted and the job's type detection stage decides.
                    detected = null;
                }

                if (detected != null && (detected.IsUnknown || !DocumentAtomClient.CanExtract(detected.Type)))
                {
                    string kind = detected.IsUnknown ? "an unrecognized type" : "type '" + detected.Type + "', which cannot be extracted";
                    return Reject(result, 415, "'" + fileName + "' is " + kind + ".");
                }
            }

            string? title = TextSanitizer.Clean(request.Title);
            if (String.IsNullOrWhiteSpace(title)) title = fileName;
            return await StoreAsync(result, userId, subject, data, contentType, fileName, title, externalKey, request.Labels, request.Tags, token).ConfigureAwait(false);
        }

        /// <summary>The upload limits and advisory types, for <c>GET /v1.0/ingestion/upload-settings</c>.</summary>
        /// <returns>The settings.</returns>
        public UploadSettings GetUploadSettings()
        {
            return new UploadSettings
            {
                MaxUploadBytes = _Settings.MaxUploadBytes,
                MaxFileNameLength = UploadFileTypes.MaxFileNameLength,
                DetectsTypeOnUpload = _Atomizer != null,
                SupportedTypes = UploadFileTypes.Supported()
            };
        }

        /// <summary>The message for a file over <c>Ingestion.MaxUploadBytes</c>.</summary>
        /// <param name="length">File size in bytes.</param>
        /// <returns>The message.</returns>
        public string TooLargeMessage(long length)
        {
            return "The file is " + length + " bytes; the limit is " + _Settings.MaxUploadBytes + " (Ingestion.MaxUploadBytes).";
        }

        #endregion

        #region Private-Methods

        private async Task<ContentSubmitResult> StoreAsync(
            ContentSubmitResult result,
            string? userId,
            Subject subject,
            byte[] bytes,
            string contentType,
            string? fileName,
            string? title,
            string? externalKey,
            List<string>? submittedLabels,
            Dictionary<string, string>? submittedTags,
            CancellationToken token)
        {
            string tenantId = subject.TenantId;
            List<string> labels = CleanLabels(submittedLabels);
            Dictionary<string, string> tags = CleanTags(submittedTags);

            SubjectLink? existing = externalKey == null ? null : await _Db.SubjectLinks.ReadByExternalKeyAsync(tenantId, subject.Id, externalKey, token).ConfigureAwait(false);
            if (existing != null && existing.DeletionStatus != LinkDeletionStatusEnum.None) return Reject(result, 409, "The content with this externalKey is being deleted.");
            if (existing != null && existing.SourceKind == SourceKindEnum.Crawl) return Reject(result, 409, "This externalKey belongs to a link a crawl plan manages.");

            if (existing != null)
            {
                await _Blobs.WriteAsync(InlineContentKeys.KeyFor(tenantId, existing.Id), bytes, token).ConfigureAwait(false);
                existing.SourceKind = SourceKindEnum.Inline;
                existing.Url = InlineContentKeys.UriFor(existing.Id, fileName);
                existing.Title = title ?? existing.Title;
                existing.ContentType = contentType;
                existing.FileName = fileName;
                existing.SizeBytes = bytes.Length;
                existing.Labels = labels;
                existing.Tags = tags;
                existing.ContentHash = null;
                existing.Status = SubjectLinkStatusEnum.Submitted;
                existing.LastError = null;
                await _Db.SubjectLinks.UpdateAsync(existing, token).ConfigureAwait(false);
                IngestionJob replacement = await _Db.IngestionJobs.CreateAsync(NewJob(subject, existing), token).ConfigureAwait(false);
                result.StatusCode = 200;
                result.Replaced = true;
                result.Link = existing;
                result.JobId = replacement.Id;
                return result;
            }

            SubjectLink link = new SubjectLink
            {
                TenantId = tenantId,
                SubjectId = subject.Id,
                Title = title,
                Labels = labels,
                Tags = tags,
                SubmittedByUserId = userId,
                Status = SubjectLinkStatusEnum.Submitted,
                SourceKind = SourceKindEnum.Inline,
                ExternalKey = externalKey,
                ContentType = contentType,
                FileName = fileName,
                SizeBytes = bytes.Length
            };
            link.Url = InlineContentKeys.UriFor(link.Id, fileName);

            string key = InlineContentKeys.KeyFor(tenantId, link.Id);
            await _Blobs.WriteAsync(key, bytes, token).ConfigureAwait(false);
            try
            {
                IngestionJob job = NewJob(subject, link);
                result.Link = await _Db.SubjectLinks.CreateWithJobAsync(link, job, token).ConfigureAwait(false);
                result.JobId = job.Id;
                result.StatusCode = 201;
                return result;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // Do not leave the stored content behind when the link could not be created (for example a concurrent
                // push with the same external key won the unique index).
                await _Blobs.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private static IngestionJob NewJob(Subject subject, SubjectLink link)
        {
            return new IngestionJob
            {
                TenantId = link.TenantId,
                SubjectId = subject.Id,
                LinkId = link.Id,
                SourceUrl = link.Url,
                Labels = new List<string>(link.Labels),
                Tags = new Dictionary<string, string>(link.Tags),
                Status = IngestionStatusEnum.Queued,
                Stage = IngestionStageEnum.Pending,
                EmbeddingEndpointId = subject.EmbeddingModel,
                CompletionEndpointId = subject.InferenceModel,
                CollectionId = subject.Collection
            };
        }

        private static List<string> CleanLabels(List<string>? labels)
        {
            List<string> result = new List<string>();
            if (labels == null) return result;
            foreach (string label in labels)
            {
                string? clean = TextSanitizer.Clean(label)?.Trim();
                if (!String.IsNullOrEmpty(clean) && !result.Contains(clean, StringComparer.Ordinal)) result.Add(clean);
            }

            return result;
        }

        private static Dictionary<string, string> CleanTags(Dictionary<string, string>? tags)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tags == null) return result;
            foreach (KeyValuePair<string, string> tag in tags)
            {
                string? key = TextSanitizer.Clean(tag.Key)?.Trim();
                if (String.IsNullOrEmpty(key)) continue;
                result[key] = (TextSanitizer.Clean(tag.Value) ?? String.Empty).Trim();
            }

            return result;
        }

        private static ContentSubmitResult Reject(ContentSubmitResult result, int status, string error)
        {
            result.StatusCode = status;
            result.Error = error;
            return result;
        }

        #endregion
    }
}
