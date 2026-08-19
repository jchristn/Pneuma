namespace Pneuma.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;

    /// <summary>
    /// Provisions a tenant's first administrator and the records that make the tenant immediately usable:
    /// a tenant-admin <see cref="User"/>, a <see cref="UserRoleAssignment"/> granting the global built-in
    /// TenantAdmin role, and a default API-key <see cref="Credential"/>. Every step is idempotent (guarded
    /// by a natural key) so it is safe to run at first boot and again whenever a tenant is created. Built-in
    /// roles are global (TenantId null) and resolve for any tenant, so no per-tenant role rows are created.
    /// </summary>
    public static class TenantProvisioner
    {
        #region Private-Members

        private const string _DefaultCredentialName = "Default API Key";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure a tenant has an administrator with the TenantAdmin role and a default API key.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cipher">Cipher used to encrypt the credential secret at rest.</param>
        /// <param name="tenantId">The tenant to provision within.</param>
        /// <param name="email">Admin email (natural key of the user within the tenant).</param>
        /// <param name="password">Admin password (hashed before storage).</param>
        /// <param name="firstName">Admin first name.</param>
        /// <param name="lastName">Admin last name.</param>
        /// <param name="isSystemAdmin">Whether the admin is also a global system administrator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioning result, including the default credential secret when newly created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<TenantProvisionResult> ProvisionTenantAdminAsync(
            DatabaseDriverBase db,
            Aes256Cipher cipher,
            string tenantId,
            string email,
            string password,
            string firstName,
            string lastName,
            bool isSystemAdmin,
            CancellationToken token = default)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));

            TenantProvisionResult result = new TenantProvisionResult();

            // 1. Ensure the tenant-admin user exists (natural key: tenant + email).
            User? user = await db.Users.ReadByEmailAsync(tenantId, email, token).ConfigureAwait(false);
            if (user == null)
            {
                user = new User
                {
                    TenantId = tenantId,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    PasswordSha256 = PasswordHasher.Hash(password),
                    IsAdmin = isSystemAdmin,
                    IsTenantAdmin = true,
                    IsProtected = isSystemAdmin,
                    Active = true
                };
                user = await db.Users.CreateAsync(user, token).ConfigureAwait(false);
                result.UserCreated = true;
            }
            result.User = user;

            // 2. Ensure the user has the global TenantAdmin role assignment.
            result.AssignmentCreated = await EnsureTenantAdminAssignmentAsync(db, tenantId, user.Id, token).ConfigureAwait(false);

            // 3. Ensure a default API-key credential exists; surface its secret only when newly created.
            await EnsureDefaultCredentialAsync(db, cipher, tenantId, user.Id, result, token).ConfigureAwait(false);

            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task<bool> EnsureTenantAdminAssignmentAsync(DatabaseDriverBase db, string tenantId, string userId, CancellationToken token)
        {
            List<UserRoleAssignment> existing = await db.UserRoleAssignments.EnumerateByUserAsync(tenantId, userId, token).ConfigureAwait(false);
            foreach (UserRoleAssignment assignment in existing)
            {
                if (String.Equals(assignment.RoleName, BuiltInRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase)) return false;
            }

            UserRoleAssignment created = new UserRoleAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                RoleName = BuiltInRoles.TenantAdmin,
                ResourceScope = ResourceScopeEnum.Tenant,
                InheritsToChildren = true,
                IsProtected = true,
                Active = true
            };
            await db.UserRoleAssignments.CreateAsync(created, token).ConfigureAwait(false);
            return true;
        }

        private static async Task EnsureDefaultCredentialAsync(DatabaseDriverBase db, Aes256Cipher cipher, string tenantId, string userId, TenantProvisionResult result, CancellationToken token)
        {
            List<Credential> existing = await db.Credentials.EnumerateByUserAsync(tenantId, userId, token).ConfigureAwait(false);
            foreach (Credential credential in existing)
            {
                if (String.Equals(credential.Name, _DefaultCredentialName, StringComparison.Ordinal)) return;
            }

            string rawSecret = KeyGenerator.GenerateSecretKey();
            Credential toCreate = new Credential
            {
                TenantId = tenantId,
                UserId = userId,
                Name = _DefaultCredentialName,
                AccessKey = KeyGenerator.GenerateAccessKey(),
                SecretKeyEncrypted = cipher.Encrypt(rawSecret),
                SecretKeyLast4 = rawSecret.Substring(rawSecret.Length - 4),
                AuthMode = CredentialAuthModeEnum.DirectHeader,
                Active = true
            };
            Credential createdCredential = await db.Credentials.CreateAsync(toCreate, token).ConfigureAwait(false);

            result.CredentialCreated = true;
            result.AccessKey = createdCredential.AccessKey;
            result.SecretKey = rawSecret;
        }

        #endregion
    }
}
