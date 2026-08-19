namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Settings;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Captures a durable record of each handled HTTP request. Building the record is synchronous
    /// (so request state is not lost), while the database write is dispatched fire-and-forget.
    /// </summary>
    public class RequestHistoryCaptureService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly RequestHistorySettings _Settings;
        private readonly LoggingModule _Logging;

        private static readonly HashSet<string> _ExactRedactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", "set-cookie", "x-password", "x-secret-key"
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the capture service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="settings">Request history settings.</param>
        /// <param name="logging">Logging module.</param>
        public RequestHistoryCaptureService(DatabaseDriverBase db, RequestHistorySettings settings, LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Settings = settings;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build and asynchronously persist a request history entry from the completed context.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        public void Capture(HttpContextBase ctx)
        {
            if (!_Settings.Enabled) return;

            try
            {
                RequestHistoryEntry entry = Build(ctx);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _Db.RequestHistory.CreateAsync(entry).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _Logging.Debug("[RequestHistoryCapture] write failed: " + e.Message);
                    }
                });
            }
            catch (Exception e)
            {
                _Logging.Debug("[RequestHistoryCapture] build failed: " + e.Message);
            }
        }

        #endregion

        #region Private-Methods

        private RequestHistoryEntry Build(HttpContextBase ctx)
        {
            RequestContext? rc = ctx.Metadata as RequestContext;

            RequestHistoryEntry entry = new RequestHistoryEntry
            {
                TenantId = rc?.TenantId,
                UserId = rc?.UserId,
                PrincipalName = rc?.DisplayName,
                Method = ctx.Request.Method.ToString(),
                Path = ctx.Request.Url.RawWithoutQuery ?? ctx.Request.Url.RawWithQuery,
                Url = ctx.Request.Url.RawWithQuery,
                StatusCode = ctx.Response.StatusCode,
                DurationMs = ctx.Timestamp.TotalMs ?? 0,
                SourceIp = ctx.Request.Source?.IpAddress,
                CreatedUtc = ctx.Timestamp.Start,
                CompletedUtc = ctx.Timestamp.End,
                RequestHeaders = RedactHeaders(ctx.Request.Headers)
            };

            string? requestBody = ctx.Request.DataAsString;
            AttachRequestBody(entry, requestBody);

            string? responseBody = ctx.Response.DataAsString;
            AttachResponseBody(entry, responseBody);

            return entry;
        }

        private void AttachRequestBody(RequestHistoryEntry entry, string? body)
        {
            if (String.IsNullOrEmpty(body)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            entry.RequestBodyBytes = bytes.Length;
            if (bytes.Length > _Settings.MaxRequestBodyBytes)
            {
                entry.RequestBody = body.Substring(0, Math.Min(body.Length, _Settings.MaxRequestBodyBytes));
                entry.RequestBodyTruncated = true;
            }
            else
            {
                entry.RequestBody = body;
            }
        }

        private void AttachResponseBody(RequestHistoryEntry entry, string? body)
        {
            if (String.IsNullOrEmpty(body)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            entry.ResponseBodyBytes = bytes.Length;
            if (bytes.Length > _Settings.MaxResponseBodyBytes)
            {
                entry.ResponseBody = body.Substring(0, Math.Min(body.Length, _Settings.MaxResponseBodyBytes));
                entry.ResponseBodyTruncated = true;
            }
            else
            {
                entry.ResponseBody = body;
            }
        }

        private static Dictionary<string, string> RedactHeaders(System.Collections.Specialized.NameValueCollection? headers)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers == null) return result;

            for (int i = 0; i < headers.Count; i++)
            {
                string? key = headers.GetKey(i);
                if (String.IsNullOrEmpty(key)) continue;
                string? value = headers.Get(i);
                result[key] = ShouldRedact(key) ? "***redacted***" : (value ?? String.Empty);
            }
            return result;
        }

        private static bool ShouldRedact(string key)
        {
            if (_ExactRedactions.Contains(key)) return true;
            string lower = key.ToLowerInvariant();
            return lower.Contains("api-key") || lower.Contains("token") || lower.Contains("password") || lower.Contains("secret");
        }

        #endregion
    }
}
