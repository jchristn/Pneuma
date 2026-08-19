namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Settings;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Establishes a typed <see cref="RequestContext"/> for each request and issues/validates
    /// session tokens. Registered as the Watson <c>AuthenticateRequest</c> hook.
    /// </summary>
    public class AuthenticationService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthSettings _Auth;
        private readonly SessionTokenCodec _Codec;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[AuthenticationService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the authentication service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="auth">Authentication settings.</param>
        /// <param name="logging">Logging module.</param>
        public AuthenticationService(DatabaseDriverBase db, AuthSettings auth, LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            _Db = db;
            _Auth = auth;
            _Logging = logging;
            _Codec = new SessionTokenCodec(auth.TokenSigningKey);
            _Cipher = new Aes256Cipher(auth.TokenSigningKey);
        }

        #endregion

        #region Public-Methods

        /// <summary>Session token codec (shared with token routes).</summary>
        public SessionTokenCodec Codec { get { return _Codec; } }

        /// <summary>Cipher for secret material (shared with credential routes).</summary>
        public Aes256Cipher Cipher { get { return _Cipher; } }

        /// <summary>
        /// Watson authentication hook. Builds the request context, authenticates any supplied scheme,
        /// stashes the context in <c>ctx.Metadata</c>, and rejects unauthenticated requests to gated routes.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        public async Task AuthenticateRequestAsync(HttpContextBase ctx)
        {
            RequestContext rc = new RequestContext
            {
                HttpMethod = ctx.Request.Method.ToString(),
                Url = ctx.Request.Url.RawWithQuery,
                SourceIp = ctx.Request.Source?.IpAddress
            };
            ctx.Metadata = rc;

            string? bearer = ExtractBearer(ctx);
            string? apiKey = Header(ctx, "x-api-key");
            string? accessKey = Header(ctx, "x-access-key");
            string? secretKey = Header(ctx, "x-secret-key");
            string? email = Header(ctx, "x-email");
            string? password = Header(ctx, "x-password");
            string? tenantHeader = Header(ctx, "x-tenant-guid");

            try
            {
                if (!String.IsNullOrEmpty(bearer))
                {
                    if (await AuthenticateBearerAsync(rc, bearer, ctx.Token).ConfigureAwait(false)) return;
                    await RejectAsync(ctx, 401, "AuthenticationFailed", "Invalid or expired token.").ConfigureAwait(false);
                    return;
                }

                if (!String.IsNullOrEmpty(apiKey))
                {
                    if (AuthenticateAdminApiKey(rc, apiKey)) return;
                    await RejectAsync(ctx, 401, "AuthenticationFailed", "Invalid API key.").ConfigureAwait(false);
                    return;
                }

                if (!String.IsNullOrEmpty(accessKey))
                {
                    if (await AuthenticateAccessKeyAsync(rc, accessKey, secretKey, ctx.Token).ConfigureAwait(false)) return;
                    await RejectAsync(ctx, 401, "AuthenticationFailed", "Invalid access key or secret.").ConfigureAwait(false);
                    return;
                }

                if (!String.IsNullOrEmpty(email) && !String.IsNullOrEmpty(password))
                {
                    User? user = await AuthenticateUserByPasswordAsync(tenantHeader, email, password, ctx.Token).ConfigureAwait(false);
                    if (user != null)
                    {
                        PopulateUser(rc, user, AuthSchemeEnum.PasswordHeaders);
                        return;
                    }
                    await RejectAsync(ctx, 401, "AuthenticationFailed", "Invalid credentials.").ConfigureAwait(false);
                    return;
                }

                await RejectAsync(ctx, 401, "AuthenticationRequired", "No authentication material supplied.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "exception during authentication: " + e.Message);
                await RejectAsync(ctx, 500, "InternalError", "Authentication error.").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Authenticate a user by email and password, optionally scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null to search all tenants for the email.</param>
        /// <param name="email">Email address.</param>
        /// <param name="password">Plaintext password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The authenticated, active user, or null.</returns>
        public async Task<User?> AuthenticateUserByPasswordAsync(string? tenantId, string email, string password, System.Threading.CancellationToken token = default)
        {
            User? user = null;

            if (!String.IsNullOrEmpty(tenantId))
            {
                user = await _Db.Users.ReadByEmailAsync(tenantId, email, token).ConfigureAwait(false);
            }
            else
            {
                List<Tenant> tenants = await _Db.Tenants.EnumerateAsync(token).ConfigureAwait(false);
                foreach (Tenant t in tenants)
                {
                    User? candidate = await _Db.Users.ReadByEmailAsync(t.Id, email, token).ConfigureAwait(false);
                    if (candidate != null) { user = candidate; break; }
                }
            }

            if (user == null || !user.Active) return null;
            if (!PasswordHasher.Verify(password, user.PasswordSha256)) return null;
            return user;
        }

        /// <summary>
        /// Issue a session token for a user and persist the backing session record.
        /// </summary>
        /// <param name="user">Authenticated user.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="userAgent">User agent.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The opaque session token and its expiration.</returns>
        public async Task<AuthSession> IssueUserSessionAsync(User user, string? sourceIp, string? userAgent, System.Threading.CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            DateTime now = DateTime.UtcNow;
            AuthSession session = new AuthSession
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                PrincipalType = PrincipalTypeEnum.User,
                AuthScheme = AuthSchemeEnum.BearerToken,
                TokenId = Guid.NewGuid().ToString("N"),
                SourceIp = sourceIp,
                UserAgent = userAgent,
                ExpiresUtc = now.AddMinutes(_Auth.TokenLifetimeMinutes)
            };
            await _Db.Sessions.CreateAsync(session, token).ConfigureAwait(false);
            return session;
        }

        /// <summary>Build the opaque token string for a session.</summary>
        /// <param name="session">Session.</param>
        /// <returns>Opaque token.</returns>
        public string EncodeSessionToken(AuthSession session)
        {
            TokenPayload payload = new TokenPayload
            {
                SessionId = session.Id,
                PrincipalType = session.PrincipalType,
                TenantId = session.TenantId,
                UserId = session.UserId,
                CredentialId = session.CredentialId,
                AdministratorId = session.AdministratorId,
                AccountId = session.AccountId,
                TokenId = session.TokenId,
                Scheme = session.AuthScheme,
                IssuedUtc = session.CreatedUtc,
                ExpiresUtc = session.ExpiresUtc
            };
            return _Codec.Encode(payload);
        }

        #endregion

        #region Private-Methods

        private async Task<bool> AuthenticateBearerAsync(RequestContext rc, string bearer, System.Threading.CancellationToken token)
        {
            TokenPayload? payload = _Codec.Decode(bearer);
            if (payload == null) return false;
            if (payload.ExpiresUtc < DateTime.UtcNow) return false;

            AuthSession? session = await _Db.Sessions.ReadAsync(payload.SessionId, token).ConfigureAwait(false);
            if (session == null || !session.Active) return false;
            if (session.RevokedUtc != null) return false;
            if (session.ExpiresUtc < DateTime.UtcNow) return false;

            if (session.PrincipalType == PrincipalTypeEnum.User && !String.IsNullOrEmpty(session.UserId))
            {
                User? user = await _Db.Users.ReadByIdAsync(session.UserId, token).ConfigureAwait(false);
                if (user == null || !user.Active) return false;
                PopulateUser(rc, user, AuthSchemeEnum.BearerToken);
                rc.Authentication.SessionId = session.Id;
                return true;
            }

            if (session.PrincipalType == PrincipalTypeEnum.Credential && !String.IsNullOrEmpty(session.CredentialId))
            {
                Credential? credential = await _Db.Credentials.ReadByAccessKeyAsync(session.TokenId, token).ConfigureAwait(false);
                if (credential == null) return false;
                PopulateCredential(rc, credential, AuthSchemeEnum.BearerToken);
                rc.Authentication.SessionId = session.Id;
                return true;
            }

            return false;
        }

        private bool AuthenticateAdminApiKey(RequestContext rc, string apiKey)
        {
            foreach (string candidate in _Auth.AdminApiKeys)
            {
                if (FixedEquals(candidate, apiKey))
                {
                    rc.IsAuthenticated = true;
                    rc.IsAdmin = true;
                    rc.DisplayName = "System Administrator";
                    rc.Authentication.Result = Pneuma.Core.Enums.AuthenticationResultEnum.Success;
                    rc.Authentication.Scheme = AuthSchemeEnum.AdminApiKey;
                    rc.Authentication.PrincipalType = PrincipalTypeEnum.Administrator;
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> AuthenticateAccessKeyAsync(RequestContext rc, string accessKey, string? secretKey, System.Threading.CancellationToken token)
        {
            if (String.IsNullOrEmpty(secretKey)) return false;
            Credential? credential = await _Db.Credentials.ReadByAccessKeyAsync(accessKey, token).ConfigureAwait(false);
            if (credential == null || !credential.Active) return false;
            if (credential.ExpiresUtc != null && credential.ExpiresUtc < DateTime.UtcNow) return false;

            string storedSecret;
            try { storedSecret = _Cipher.Decrypt(credential.SecretKeyEncrypted); }
            catch (Exception) { return false; }

            if (!FixedEquals(storedSecret, secretKey)) return false;

            PopulateCredential(rc, credential, AuthSchemeEnum.AccessKeySecret);
            return true;
        }

        private void PopulateUser(RequestContext rc, User user, AuthSchemeEnum scheme)
        {
            rc.IsAuthenticated = true;
            rc.TenantId = user.TenantId;
            rc.UserId = user.Id;
            rc.IsAdmin = user.IsAdmin;
            rc.IsTenantAdmin = user.IsTenantAdmin;
            rc.Email = user.Email;
            rc.DisplayName = (user.FirstName + " " + user.LastName).Trim();
            rc.Authentication.Result = Pneuma.Core.Enums.AuthenticationResultEnum.Success;
            rc.Authentication.Scheme = scheme;
            rc.Authentication.PrincipalType = PrincipalTypeEnum.User;
            rc.Authentication.PrincipalId = user.Id;
            rc.Authentication.User = user;
        }

        private void PopulateCredential(RequestContext rc, Credential credential, AuthSchemeEnum scheme)
        {
            rc.IsAuthenticated = true;
            rc.TenantId = credential.TenantId;
            rc.UserId = credential.UserId;
            rc.DisplayName = credential.Name;
            rc.Authentication.Result = Pneuma.Core.Enums.AuthenticationResultEnum.Success;
            rc.Authentication.Scheme = scheme;
            rc.Authentication.PrincipalType = PrincipalTypeEnum.Credential;
            rc.Authentication.PrincipalId = credential.Id;
            rc.Authentication.Credential = credential;
        }

        private static string? ExtractBearer(HttpContextBase ctx)
        {
            string? auth = Header(ctx, "authorization");
            if (!String.IsNullOrEmpty(auth))
            {
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return auth.Substring(7).Trim();
                }
                return auth.Trim();
            }
            return Header(ctx, "x-token");
        }

        private static string? Header(HttpContextBase ctx, string name)
        {
            if (ctx.Request.Headers == null) return null;
            string? value = ctx.Request.Headers[name];
            return String.IsNullOrEmpty(value) ? null : value;
        }

        private async Task RejectAsync(HttpContextBase ctx, int status, string code, string message)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(Json.Serialize(new ErrorResponse(code, message))).ConfigureAwait(false);
        }

        private static bool FixedEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length) return false;
            return CryptographicOperations.FixedTimeEquals(ba, bb);
        }

        #endregion
    }
}
