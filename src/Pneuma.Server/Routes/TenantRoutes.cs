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
    /// Tenant control-plane routes. Require Admin on the Tenant resource (global admin bypass).
    /// </summary>
    public class TenantRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly Aes256Cipher _Cipher;
        private readonly TenantProvisioningService _Provisioning;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate tenant routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="cipher">Cipher used to encrypt provisioned credential secrets.</param>
        /// <param name="provisioning">Tenant provisioning service that creates subordinate-service resources on tenant creation.</param>
        public TenantRoutes(DatabaseDriverBase db, AuthorizationService authz, Aes256Cipher cipher, TenantProvisioningService provisioning)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (provisioning == null) throw new ArgumentNullException(nameof(provisioning));
            _Db = db;
            _Authz = authz;
            _Cipher = cipher;
            _Provisioning = provisioning;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/tenants", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List tenants", "Tenants"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/tenants", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/tenants/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/tenants/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/tenants/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a tenant", "Tenants"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Tenant, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            List<Tenant> tenants = await _Db.Tenants.EnumerateAsync(ctx.Token).ConfigureAwait(false);
            EnumerationResult<Tenant> result = EnumerationHelper.Paginate(tenants, RouteHelper.ReadEnumerationQuery(ctx), t => t.CreatedUtc, t => t.Name);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Tenant, OperationTypeEnum.Create, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            CreateTenantRequest? request = RouteHelper.ReadBody<CreateTenantRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Name))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant name is required.").ConfigureAwait(false);
                return;
            }

            Tenant tenant = new Tenant
            {
                AccountId = request.AccountId,
                ParentId = request.ParentId,
                Name = request.Name,
                Region = request.Region,
                Active = request.Active
            };
            Tenant created = await _Db.Tenants.CreateAsync(tenant, ctx.Token).ConfigureAwait(false);

            // Provision the tenant's resources on subordinate services (RecallDB tenant + default collection).
            // Best-effort: an unavailable subordinate service must not fail tenant creation.
            await _Provisioning.ProvisionAsync(created.Id, created.Name, ctx.Token).ConfigureAwait(false);

            // Cascade: provision the tenant's first administrator, TenantAdmin assignment, and default API key.
            string adminEmail = String.IsNullOrWhiteSpace(request.AdminEmail) ? "admin@" + created.Id : request.AdminEmail!;
            bool passwordGenerated = String.IsNullOrWhiteSpace(request.AdminPassword);
            string adminPassword = passwordGenerated ? KeyGenerator.GenerateSecretKey() : request.AdminPassword!;
            string adminFirstName = String.IsNullOrWhiteSpace(request.AdminFirstName) ? "Tenant" : request.AdminFirstName!;
            string adminLastName = String.IsNullOrWhiteSpace(request.AdminLastName) ? "Administrator" : request.AdminLastName!;

            TenantProvisionResult provision = await TenantProvisioner.ProvisionTenantAdminAsync(
                _Db, _Cipher, created.Id, adminEmail, adminPassword, adminFirstName, adminLastName, false, ctx.Token).ConfigureAwait(false);

            TenantProvisionResponse response = TenantProvisionResponse.FromResult(created, provision, adminEmail, passwordGenerated ? adminPassword : null);
            await RouteHelper.SendJsonAsync(ctx, 201, response).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Tenant, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            Tenant? tenant = await _Db.Tenants.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (tenant == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, tenant).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Tenant, OperationTypeEnum.Update, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            Tenant? existing = await _Db.Tenants.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }
            Tenant? update = RouteHelper.ReadBody<Tenant>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            existing.Name = String.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name;
            existing.Region = update.Region;
            existing.Active = update.Active;
            Tenant saved = await _Db.Tenants.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Tenant, OperationTypeEnum.Delete, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            Tenant? existing = await _Db.Tenants.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing != null && existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Tenant is protected.").ConfigureAwait(false);
                return;
            }
            bool deleted = await _Db.Tenants.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
