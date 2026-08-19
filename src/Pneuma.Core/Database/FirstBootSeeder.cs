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
    /// Seeds default records into an empty database. Every step is idempotent: it checks for an
    /// existing record by natural key before creating one, so it is safe to run on every boot.
    /// </summary>
    public static class FirstBootSeeder
    {
        #region Public-Methods

        /// <summary>
        /// Seed built-in roles, the default account/tenant/administrator, default prompts, and provision the
        /// system tenant's administrator with the TenantAdmin role assignment and a default API key.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="options">Seed options.</param>
        /// <param name="cipher">Cipher used to encrypt the default credential secret.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The system-admin provisioning result (including the default API key when newly created), or null when no tenant was seeded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> or <paramref name="cipher"/> is null.</exception>
        public static async Task<TenantProvisionResult?> SeedAsync(DatabaseDriverBase db, SeedOptions options, Aes256Cipher cipher, CancellationToken token = default)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (options == null) options = new SeedOptions();

            await SeedRolesAsync(db, token).ConfigureAwait(false);
            string tenantId = await SeedAccountTenantAdminAsync(db, options, token).ConfigureAwait(false);
            await SeedPromptsAsync(db, token).ConfigureAwait(false);

            if (String.IsNullOrEmpty(tenantId)) return null;

            return await TenantProvisioner.ProvisionTenantAdminAsync(
                db, cipher, tenantId, options.AdminEmail, options.AdminPassword,
                options.AdminFirstName, options.AdminLastName, true, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task SeedRolesAsync(DatabaseDriverBase db, CancellationToken token)
        {
            foreach (string roleName in BuiltInRoles.Names())
            {
                UserRole? existing = await db.Roles.ReadByNameAsync(null, roleName, token).ConfigureAwait(false);
                if (existing != null) continue;

                UserRole role = new UserRole
                {
                    TenantId = null,
                    Name = roleName,
                    IsBuiltIn = true,
                    IsProtected = true,
                    Active = true
                };

                List<Permission> permissions = new List<Permission>();
                List<RolePermissionMap> maps = new List<RolePermissionMap>();
                foreach (PermissionSpec spec in BuiltInRoles.DefaultPermissions(roleName))
                {
                    Permission permission = new Permission
                    {
                        TenantId = null,
                        Name = roleName + " defaults",
                        ResourceTypes = spec.ResourceTypes,
                        OperationTypes = spec.OperationTypes,
                        PermissionType = spec.PermissionType,
                        IsProtected = true,
                        Active = true
                    };
                    permissions.Add(permission);

                    maps.Add(new RolePermissionMap
                    {
                        TenantId = null,
                        RoleId = role.Id,
                        PermissionId = permission.Id,
                        IsProtected = true,
                        Active = true
                    });
                }

                // Create the role and all of its permissions and maps in one transaction so a role is never
                // left half-provisioned if seeding is interrupted mid-write.
                await db.Roles.CreateWithPermissionsAsync(role, permissions, maps, token).ConfigureAwait(false);
            }
        }

        private static async Task<string> SeedAccountTenantAdminAsync(DatabaseDriverBase db, SeedOptions options, CancellationToken token)
        {
            List<Account> accounts = await db.Accounts.EnumerateAsync(token).ConfigureAwait(false);
            Account account;
            if (accounts.Count == 0)
            {
                account = new Account { Name = options.AccountName, IsProtected = true, Active = true };
                await db.Accounts.CreateAsync(account, token).ConfigureAwait(false);
            }
            else
            {
                account = accounts[0];
            }

            List<Tenant> tenants = await db.Tenants.EnumerateAsync(token).ConfigureAwait(false);
            Tenant? tenant = tenants.Find(t => t.Name == options.TenantName);
            if (tenant == null)
            {
                tenant = new Tenant { AccountId = account.Id, Name = options.TenantName, IsProtected = true, Active = true };
                await db.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
            }

            string passwordHash = PasswordHasher.Hash(options.AdminPassword);

            Administrator? admin = await db.Administrators.ReadByEmailAsync(options.AdminEmail, token).ConfigureAwait(false);
            if (admin == null)
            {
                admin = new Administrator
                {
                    AccountId = account.Id,
                    FirstName = options.AdminFirstName,
                    LastName = options.AdminLastName,
                    Email = options.AdminEmail,
                    PasswordSha256 = passwordHash,
                    IsProtected = true,
                    Active = true
                };
                await db.Administrators.CreateAsync(admin, token).ConfigureAwait(false);
            }

            User? adminUser = await db.Users.ReadByEmailAsync(tenant.Id, options.AdminEmail, token).ConfigureAwait(false);
            if (adminUser == null)
            {
                adminUser = new User
                {
                    TenantId = tenant.Id,
                    FirstName = options.AdminFirstName,
                    LastName = options.AdminLastName,
                    Email = options.AdminEmail,
                    PasswordSha256 = passwordHash,
                    IsAdmin = true,
                    IsTenantAdmin = true,
                    IsProtected = true,
                    Active = true
                };
                await db.Users.CreateAsync(adminUser, token).ConfigureAwait(false);
            }

            return tenant.Id;
        }

        private static async Task SeedPromptsAsync(DatabaseDriverBase db, CancellationToken token)
        {
            await SeedPromptAsync(db, "ontology.classify", "Ontology Classification",
                "You are Pneuma's knowledge-graph classifier. Given the semantic cells extracted from a subject's " +
                "artifact, produce a JSON subgraph that conforms to the ontology described below. Resolve entities to " +
                "canonical names so the same person/work/place is not duplicated, and set a confidence for each node and " +
                "edge. Only assert what the source supports.", token).ConfigureAwait(false);

            await SeedPromptAsync(db, "ontology.definition", "Ontology Definition",
                "The ontology describes a musical subject's world. Define entities (nodes) and how they relate (edges) " +
                "in natural language; an administrator may rewrite this to reshape the graph without any code change.\n\n" +
                "Node types:\n" +
                "- Subject: the subject the archive is about.\n" +
                "- Person: a collaborator, producer, influence, or other individual.\n" +
                "- Organization: a label, band (e.g. Public Enemy), venue operator, or media outlet.\n" +
                "- Discography: the container for a subject's released body of work.\n" +
                "- Record: an album, EP, or single.\n" +
                "- Track: an individual song.\n" +
                "- Lyrics: the lyric content of a track.\n" +
                "- Work: a non-musical creative work such as a book, essay, or artwork.\n" +
                "- Event: a concert, interview, broadcast, or public appearance.\n" +
                "- Place: a venue, city, or location.\n" +
                "- Theme: a recurring topic or motif (e.g. activism, media critique).\n" +
                "- CulturalMoment: a historical or cultural moment the work engages with.\n" +
                "- Source: the artifact a claim came from (used for provenance).\n" +
                "- Media: a retrievable media asset (audio, video, image).\n\n" +
                "Relationship types (edges), written FROM -> TO:\n" +
                "- HAS_DISCOGRAPHY (Subject -> Discography), CONTAINS_RECORD (Discography -> Record), " +
                "HAS_TRACK (Record -> Track), HAS_LYRICS (Track -> Lyrics).\n" +
                "- PERFORMED_BY (Work -> Person/Subject), PRODUCED_BY (Work -> Person), COLLABORATED_WITH (Person -> Person), " +
                "MEMBER_OF (Person -> Organization), RELEASED_ON (Record -> Organization).\n" +
                "- PERFORMED_AT (Event -> Place), OCCURRED_ON (Event -> CulturalMoment), ABOUT_THEME (any -> Theme), " +
                "REFERENCES_MOMENT (any -> CulturalMoment), INFLUENCED_BY (any -> Person/Work), HAS_MEDIA (any -> Media), " +
                "MENTIONS (any -> any).\n\n" +
                "When information is present but does not fit an existing type, prefer the closest listed type rather than " +
                "inventing an unrelated one.", token).ConfigureAwait(false);

            await SeedPromptAsync(db, "cell.summarize", "Cell Summarization",
                "Summarize the following content faithfully and concisely, preserving names, dates, places, and claims. " +
                "Do not add information that is not present in the source.", token).ConfigureAwait(false);

            await SeedPromptAsync(db, "user.answer", "User Answer",
                "You are answering a fan's question about a subject using only the provided source excerpts from Pneuma's " +
                "curated corpus. Ground every statement in the sources and cite them. If the corpus does not support an " +
                "answer, say so plainly rather than guessing.", token).ConfigureAwait(false);

            await SeedPromptAsync(db, "assistant.system", "Assistant System Prompt", DefaultAssistantSystemPrompt, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Default system prompt for the agentic chat assistant (Prompts key "assistant.system"). It tells the
        /// model how to use Pneuma's read tools — the tools are supplied to the model through native function
        /// calling, so this describes when to call each one, the paging protocol, and grounding rules. An
        /// administrator may edit it on the Prompts page to reshape assistant behavior without a code change.
        /// </summary>
        private const string DefaultAssistantSystemPrompt =
            "You are Pneuma's knowledge assistant. Pneuma is a knowledge-graph platform: ingested sources are turned into a " +
            "graph of nodes (people, works, events, places, themes, and more) that you can search and traverse. Answer the " +
            "user's questions about this corpus accurately and concisely, grounding your statements in what the tools return.\n\n" +
            "TOOLS\n" +
            "You have access to the following read-only Pneuma tools via function calling. Request a tool by calling it with " +
            "JSON arguments that match its schema; you will receive the tool's JSON result and may then call more tools or " +
            "answer. Never invent tool names or results.\n" +
            "- pneuma_search { query, max? } - full-text search the corpus; returns ranked node summaries (id, name, type, score). Start here for most questions.\n" +
            "- pneuma_get_node { id } - fetch one graph node's full content, labels, and tags.\n" +
            "- pneuma_get_neighbors { id } - fetch a node's adjacent nodes (id, name, type) to explore relationships.\n" +
            "- pneuma_enumerate_subjects { maxResults?, skip?, order?, search? } - list the subjects the corpus is about.\n" +
            "- pneuma_get_subject { id } - fetch one subject in full.\n" +
            "- pneuma_enumerate_links { maxResults?, skip?, order? } / pneuma_get_link { id } - list or fetch ingestion source links.\n" +
            "- pneuma_enumerate_jobs { maxResults?, skip?, order? } / pneuma_get_job { id } - list or fetch ingestion jobs (status, stage, error).\n" +
            "- pneuma_capabilities {} - describe the platform and the paging protocol.\n\n" +
            "PAGING\n" +
            "Enumerations are paged. Call an enumerate tool with skip=0; the first result's totalRecords is the exact total. " +
            "Advance skip by the page size and call again until endOfResults is true. Fetch full objects individually with the " +
            "matching get tool.\n\n" +
            "HOW TO ANSWER\n" +
            "1. Decide which tools are needed; prefer pneuma_search to locate nodes, then pneuma_get_node for detail. Use as few " +
            "calls as will answer the question well.\n" +
            "2. Ground every claim in tool results. When you use a node, refer to it by name so the user can follow the source.\n" +
            "3. If the corpus does not contain enough information, say so plainly rather than guessing.\n" +
            "4. Format answers in Markdown (headings, lists, tables, and fenced code where helpful). Be concise and direct.";

        private static async Task SeedPromptAsync(DatabaseDriverBase db, string key, string name, string content, CancellationToken token)
        {
            Prompt? existing = await db.Prompts.ReadByKeyAsync(null, key, token).ConfigureAwait(false);
            if (existing != null) return;

            Prompt prompt = new Prompt
            {
                TenantId = null,
                Key = key,
                Name = name,
                Content = content,
                Version = 1,
                IsProtected = true,
                Active = true
            };
            await db.Prompts.CreateAsync(prompt, token).ConfigureAwait(false);
        }

        #endregion
    }
}
