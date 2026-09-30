namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using WatsonWebserver.Core;

    /// <summary>Helpers the ontology route registrars share: failure responses and governance audit records.</summary>
    internal static class OntologyRouteSupport
    {
        #region Internal-Methods

        /// <summary>Send a failed result as a JSON error (with its problems) and return false; return true when it succeeded.</summary>
        /// <typeparam name="T">Result value type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="result">The result.</param>
        /// <returns>True when the result succeeded and nothing was sent.</returns>
        internal static async Task<bool> SendFailureAsync<T>(HttpContextBase ctx, OntologyResult<T> result)
        {
            if (result.Succeeded) return true;
            string code;
            switch (result.StatusCode)
            {
                case 404: code = "NotFound"; break;
                case 409: code = "Conflict"; break;
                case 413: code = "PayloadTooLarge"; break;
                case 502: code = "BadGateway"; break;
                default: code = "BadRequest"; break;
            }
            ErrorResponse error = new ErrorResponse(code, result.Error ?? "Request refused.");
            if (result.Problems.Count > 0) error.Problems = result.Problems;
            ctx.Response.StatusCode = result.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(Json.Serialize(error)).ConfigureAwait(false);
            return false;
        }

        /// <summary>Parse an RDF format query value (turtle or jsonld); null when it is neither.</summary>
        /// <param name="value">The query value; empty means Turtle.</param>
        /// <returns>The format, or null.</returns>
        internal static RdfFormatEnum? RdfFormat(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "turtle", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "ttl", StringComparison.OrdinalIgnoreCase)) return RdfFormatEnum.Turtle;
            if (String.Equals(value, "jsonld", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "json-ld", StringComparison.OrdinalIgnoreCase)) return RdfFormatEnum.JsonLd;
            return null;
        }

        /// <summary>Parse a graph export format query value (json, jsonld, turtle, or graphml); null when it is none of them.</summary>
        /// <param name="value">The query value; empty means Pneuma JSON.</param>
        /// <returns>The format, or null.</returns>
        internal static GraphExportFormatEnum? GraphFormat(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "json", StringComparison.OrdinalIgnoreCase)) return GraphExportFormatEnum.Json;
            if (String.Equals(value, "graphml", StringComparison.OrdinalIgnoreCase)) return GraphExportFormatEnum.GraphMl;
            RdfFormatEnum? rdf = RdfFormat(value);
            if (rdf == RdfFormatEnum.JsonLd) return GraphExportFormatEnum.JsonLd;
            if (rdf == RdfFormatEnum.Turtle) return GraphExportFormatEnum.Turtle;
            return null;
        }

        /// <summary>Whether a base IRI is absent or an absolute IRI that can prefix resource names.</summary>
        /// <param name="value">The base IRI.</param>
        /// <returns>True when usable.</returns>
        internal static bool ValidBaseIri(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return true;
            Uri? parsed;
            return value!.Length <= 512 && Uri.TryCreate(value, UriKind.Absolute, out parsed);
        }

        /// <summary>Record an ontology governance event (approval, retirement, or pin change). Best-effort.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rc">Request context.</param>
        /// <param name="resource">The resource type that was acted on.</param>
        /// <param name="operation">The operation performed.</param>
        /// <param name="resourceId">The version or subject identifier.</param>
        /// <param name="note">What happened, in words.</param>
        internal static async Task AuditAsync(DatabaseDriverBase db, HttpContextBase ctx, RequestContext rc, ResourceTypeEnum resource, OperationTypeEnum operation, string resourceId, string note)
        {
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.OntologyGovernance,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    ResourceId = resourceId,
                    RequiredResourceType = resource,
                    RequiredOperation = operation,
                    HttpMethod = ctx.Request.Method.ToString(),
                    UrlPath = ctx.Request.Url.RawWithoutQuery,
                    SourceIp = ctx.Request.Source?.IpAddress,
                    DenialReason = note
                };
                await db.Audit.CreateAsync(record, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // Audit is best-effort; the action stands either way.
            }
        }

        #endregion
    }
}
