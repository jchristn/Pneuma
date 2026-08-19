namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Session token routes: create (anonymous login), validate, details, and revoke.
    /// </summary>
    public class AuthRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthenticationService _Auth;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate auth routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="auth">Authentication service.</param>
        public AuthRoutes(DatabaseDriverBase db, AuthenticationService auth)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            _Db = db;
            _Auth = auth;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/token", CreateTokenAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a session token", "Tokens"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/token", ValidateTokenAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Validate the current token", "Tokens"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/token/details", TokenDetailsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get current token details", "Tokens"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/token/refresh", RefreshTokenAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Refresh (rotate) the current session", "Tokens"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, "/v1.0/token", RevokeTokenAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Revoke the current session", "Tokens"));
        }

        #endregion

        #region Private-Methods

        private async Task CreateTokenAsync(HttpContextBase ctx)
        {
            LoginRequest? request = RouteHelper.ReadBody<LoginRequest>(ctx);
            string? email = request?.Email;
            string? password = request?.Password;
            string? tenantId = request?.TenantId;

            if (String.IsNullOrEmpty(email)) email = ctx.Request.Headers["x-email"];
            if (String.IsNullOrEmpty(password)) password = ctx.Request.Headers["x-password"];
            if (String.IsNullOrEmpty(tenantId)) tenantId = ctx.Request.Headers["x-tenant-guid"];

            if (String.IsNullOrEmpty(email) || String.IsNullOrEmpty(password))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Email and password are required.").ConfigureAwait(false);
                return;
            }

            User? user = await _Auth.AuthenticateUserByPasswordAsync(tenantId, email, password, ctx.Token).ConfigureAwait(false);
            if (user == null)
            {
                await WriteAuthAuditAsync(AuditEventTypeEnum.AuthFailure, tenantId, null, null, ctx).ConfigureAwait(false);
                await RouteHelper.SendErrorAsync(ctx, 401, "AuthenticationFailed", "Invalid credentials.").ConfigureAwait(false);
                return;
            }

            string? userAgent = ctx.Request.Headers["user-agent"];
            AuthSession session = await _Auth.IssueUserSessionAsync(user, ctx.Request.Source?.IpAddress, userAgent, ctx.Token).ConfigureAwait(false);
            string token = _Auth.EncodeSessionToken(session);
            await WriteAuthAuditAsync(AuditEventTypeEnum.AuthSuccess, user.TenantId, user.Id, session.Id, ctx).ConfigureAwait(false);
            await WriteAuthAuditAsync(AuditEventTypeEnum.SessionIssued, user.TenantId, user.Id, session.Id, ctx).ConfigureAwait(false);

            TokenResponse response = new TokenResponse
            {
                Token = token,
                ExpiresUtc = session.ExpiresUtc,
                PrincipalType = session.PrincipalType,
                TenantId = user.TenantId,
                UserId = user.Id,
                DisplayName = (user.FirstName + " " + user.LastName).Trim(),
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                IsTenantAdmin = user.IsTenantAdmin
            };
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task ValidateTokenAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            TokenResponse response = new TokenResponse
            {
                Token = string.Empty,
                PrincipalType = rc.Authentication.PrincipalType ?? Core.Enums.PrincipalTypeEnum.User,
                TenantId = rc.TenantId,
                UserId = rc.UserId,
                DisplayName = rc.DisplayName,
                Email = rc.Email,
                IsAdmin = rc.IsAdmin,
                IsTenantAdmin = rc.IsTenantAdmin
            };
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task TokenDetailsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, rc.Authentication).ConfigureAwait(false);
        }

        private async Task RevokeTokenAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            string? sessionId = rc.Authentication.SessionId;
            if (String.IsNullOrEmpty(sessionId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "No session to revoke.").ConfigureAwait(false);
                return;
            }

            AuthSession? session = await _Db.Sessions.ReadAsync(sessionId, ctx.Token).ConfigureAwait(false);
            if (session != null)
            {
                session.Active = false;
                session.RevokedUtc = DateTime.UtcNow;
                session.RevocationReason = "User logout";
                await _Db.Sessions.UpdateAsync(session, ctx.Token).ConfigureAwait(false);
            }
            await WriteAuthAuditAsync(AuditEventTypeEnum.SessionRevoked, rc.TenantId, rc.UserId, sessionId, ctx).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task RefreshTokenAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            string? sessionId = rc.Authentication.SessionId;
            if (String.IsNullOrEmpty(sessionId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "No session to refresh.").ConfigureAwait(false);
                return;
            }

            if (rc.Authentication.PrincipalType != PrincipalTypeEnum.User || String.IsNullOrEmpty(rc.UserId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Only user sessions can be refreshed.").ConfigureAwait(false);
                return;
            }

            AuthSession? existing = await _Db.Sessions.ReadAsync(sessionId, ctx.Token).ConfigureAwait(false);
            if (existing == null || !existing.Active || existing.RevokedUtc != null || existing.ExpiresUtc <= DateTime.UtcNow)
            {
                await RouteHelper.SendErrorAsync(ctx, 401, "Unauthorized", "The session cannot be refreshed.").ConfigureAwait(false);
                return;
            }

            User? user = await _Db.Users.ReadByIdAsync(rc.UserId!, ctx.Token).ConfigureAwait(false);
            if (user == null || !user.Active)
            {
                await RouteHelper.SendErrorAsync(ctx, 401, "Unauthorized", "The user is no longer active.").ConfigureAwait(false);
                return;
            }

            // Rotate: issue a fresh session and revoke the old one, so a leaked prior token is not reusable.
            string? userAgent = ctx.Request.Headers["user-agent"];
            AuthSession session = await _Auth.IssueUserSessionAsync(user, ctx.Request.Source?.IpAddress, userAgent, ctx.Token).ConfigureAwait(false);
            string token = _Auth.EncodeSessionToken(session);

            existing.Active = false;
            existing.RevokedUtc = DateTime.UtcNow;
            existing.RevocationReason = "Refreshed (rotated)";
            existing.LastUsedUtc = DateTime.UtcNow;
            await _Db.Sessions.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);

            await WriteAuthAuditAsync(AuditEventTypeEnum.SessionRefreshed, user.TenantId, user.Id, session.Id, ctx).ConfigureAwait(false);

            TokenResponse response = new TokenResponse
            {
                Token = token,
                ExpiresUtc = session.ExpiresUtc,
                PrincipalType = session.PrincipalType,
                TenantId = user.TenantId,
                UserId = user.Id,
                DisplayName = (user.FirstName + " " + user.LastName).Trim(),
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                IsTenantAdmin = user.IsTenantAdmin
            };
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task WriteAuthAuditAsync(AuditEventTypeEnum eventType, string? tenantId, string? userId, string? sessionId, HttpContextBase ctx)
        {
            // Best-effort: an audit-write failure must never block authentication.
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = eventType,
                    TenantId = tenantId,
                    UserId = userId,
                    SessionId = sessionId,
                    SourceIp = ctx.Request.Source?.IpAddress
                };
                await _Db.Audit.CreateAsync(record, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallow — audit is complementary; login/logout must still succeed.
            }
        }

        #endregion
    }
}
