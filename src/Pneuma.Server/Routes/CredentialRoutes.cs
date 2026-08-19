namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Credential (API key) management routes. The raw secret is returned only once, at creation.
    /// </summary>
    public class CredentialRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly Aes256Cipher _Cipher;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate credential routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="auth">Authentication service (for secret encryption).</param>
        public CredentialRoutes(DatabaseDriverBase db, AuthorizationService authz, AuthenticationService auth)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            _Db = db;
            _Authz = authz;
            _Cipher = auth.Cipher;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/credentials", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List credentials", "Credentials"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/credentials", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/credentials/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/credentials/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a credential", "Credentials"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Credential, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            List<Credential> credentials = await _Db.Credentials.EnumerateAsync(tenantId, ctx.Token).ConfigureAwait(false);
            List<CredentialResponse> response = new List<CredentialResponse>();
            foreach (Credential credential in credentials) response.Add(CredentialResponse.FromModel(credential, null));
            EnumerationResult<CredentialResponse> result = EnumerationHelper.Paginate(response, RouteHelper.ReadEnumerationQuery(ctx), c => c.CreatedUtc, c => c.Name);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;

            CreateCredentialRequest? request = RouteHelper.ReadBody<CreateCredentialRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Name))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Credential name is required.").ConfigureAwait(false);
                return;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            string userId = !String.IsNullOrEmpty(request.UserId) ? request.UserId! : (rc.UserId ?? String.Empty);
            if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(userId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant and user could not be resolved.").ConfigureAwait(false);
                return;
            }

            string rawSecret = KeyGenerator.GenerateSecretKey();
            Credential credential = new Credential
            {
                TenantId = tenantId,
                UserId = userId,
                Name = request.Name,
                AccessKey = KeyGenerator.GenerateAccessKey(),
                SecretKeyEncrypted = _Cipher.Encrypt(rawSecret),
                SecretKeyLast4 = rawSecret.Substring(rawSecret.Length - 4),
                AuthMode = CredentialAuthModeEnum.DirectHeader,
                ExpiresUtc = request.ExpiresUtc
            };
            Credential created = await _Db.Credentials.CreateAsync(credential, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, CredentialResponse.FromModel(created, rawSecret)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            Credential? credential = await _Db.Credentials.ReadAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (credential == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Credential not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, CredentialResponse.FromModel(credential, null)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            bool deleted = await _Db.Credentials.DeleteAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Credential not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
