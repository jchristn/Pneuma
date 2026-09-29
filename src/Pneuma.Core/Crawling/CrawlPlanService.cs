namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Security;

    /// <summary>
    /// Validates and stores crawl plans. Secrets are pulled out of the settings object, encrypted into their own
    /// table, and never returned; a plan is only read with its secrets decrypted for a run, a test, or opening content.
    /// </summary>
    public class CrawlPlanService
    {
        #region Public-Members

        /// <summary>Most labels a plan may carry.</summary>
        public const int MaxLabels = 50;

        /// <summary>Most tags a plan may carry.</summary>
        public const int MaxTags = 50;

        /// <summary>Most include, exclude, or content-type entries in a filter list.</summary>
        public const int MaxFilterEntries = 200;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly Aes256Cipher _Cipher;
        private readonly CrawlerFactory _Crawlers;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cipher">Cipher that encrypts secrets at rest.</param>
        /// <param name="crawlers">Registered crawlers (a plan's type must have one).</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlPlanService(DatabaseDriverBase db, Aes256Cipher cipher, CrawlerFactory crawlers)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Crawlers = crawlers ?? throw new ArgumentNullException(nameof(crawlers));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a plan: name, an available type with a matching settings object, the settings' own rules, the
        /// schedule, and the size of the label, tag, and filter lists. Secrets already stored count as present.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="storedSecrets">Names of secrets already stored for the plan, or null for a new plan.</param>
        /// <returns>The problems found; empty when the plan is valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public List<string> Validate(CrawlPlan plan, ICollection<string>? storedSecrets)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            List<string> errors = new List<string>();
            if (String.IsNullOrWhiteSpace(plan.Name)) errors.Add("name is required.");
            else if (plan.Name.Length > 256) errors.Add("name is at most 256 characters.");

            if (!_Crawlers.IsAvailable(plan.Type))
            {
                errors.Add("No crawler is available for " + plan.Type + " plans on this server.");
                return errors;
            }

            object? settings = plan.SettingsObject();
            if (settings == null) errors.Add(CrawlSettingsCodec.SettingsProperty(plan.Type) + " settings are required for a " + plan.Type + " plan.");
            else errors.AddRange(CrawlSettingsCodec.Validate(settings, storedSecrets).Select(e => CrawlSettingsCodec.SettingsProperty(plan.Type) + "." + e));

            if (settings != null) errors.AddRange(_Crawlers.Get(plan.Type).CheckSettings(plan));
            errors.AddRange(CrawlScheduleCalculator.Validate(plan.Schedule));
            if (plan.Labels.Count > MaxLabels) errors.Add("labels holds at most " + MaxLabels + " entries.");
            if (plan.Tags.Count > MaxTags) errors.Add("tags holds at most " + MaxTags + " entries.");
            if (plan.Tags.Keys.Any(k => String.IsNullOrWhiteSpace(k) || k.Length > 200)) errors.Add("tag keys must be 1 to 200 characters.");
            if (plan.Filter.IncludePatterns.Count > MaxFilterEntries || plan.Filter.ExcludePatterns.Count > MaxFilterEntries || plan.Filter.AllowedContentTypes.Count > MaxFilterEntries)
                errors.Add("filter lists hold at most " + MaxFilterEntries + " entries each.");
            if (plan.Filter.MaxSizeBytes > 0 && plan.Filter.MinSizeBytes > plan.Filter.MaxSizeBytes) errors.Add("filter.minSizeBytes is larger than filter.maxSizeBytes.");
            return errors;
        }

        /// <summary>
        /// True when a save turns robots.txt off for a Web plan (it was on, or the plan is new). Only system and tenant
        /// administrators may do that; keeping it off on an edit is allowed.
        /// </summary>
        /// <param name="existing">The stored plan, or null for a new plan.</param>
        /// <param name="incoming">The plan being saved.</param>
        /// <returns>True when robots.txt is being turned off.</returns>
        public static bool TurnsOffRobots(CrawlPlan? existing, CrawlPlan incoming)
        {
            if (incoming?.Web == null || incoming.Web.RespectRobotsTxt) return false;
            return existing?.Web == null || existing.Web.RespectRobotsTxt;
        }

        /// <summary>Clean a plan's free text (name, labels, tags, patterns) with <see cref="TextSanitizer"/>.</summary>
        /// <param name="plan">The plan.</param>
        public static void Clean(CrawlPlan plan)
        {
            if (plan == null) return;
            plan.Name = (TextSanitizer.Clean(plan.Name) ?? String.Empty).Trim();
            plan.Labels = plan.Labels
                .Select(l => (TextSanitizer.Clean(l) ?? String.Empty).Trim())
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> tag in plan.Tags)
            {
                string key = (TextSanitizer.Clean(tag.Key) ?? String.Empty).Trim();
                if (key.Length > 0) tags[key] = (TextSanitizer.Clean(tag.Value) ?? String.Empty).Trim();
            }
            plan.Tags = tags;
            plan.Filter.IncludePatterns = CleanList(plan.Filter.IncludePatterns);
            plan.Filter.ExcludePatterns = CleanList(plan.Filter.ExcludePatterns);
            plan.Filter.AllowedContentTypes = CleanList(plan.Filter.AllowedContentTypes);
        }

        /// <summary>Create a plan: store its configuration, encrypt its secrets, and compute its first run.</summary>
        /// <param name="plan">The plan (validated).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored plan and the secrets that were set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public async Task<CrawlPlanSaveResult> CreateAsync(CrawlPlan plan, CancellationToken token)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Dictionary<string, string> secrets = CrawlSettingsCodec.ExtractSecrets(plan.SettingsObject());
            CrawlSettingsCodec.ClearSecrets(plan.SettingsObject());
            plan.NextRunUtc = CrawlScheduleCalculator.NextRunUtc(plan, DateTime.UtcNow);
            await _Db.CrawlPlans.CreateAsync(plan, token).ConfigureAwait(false);
            foreach (KeyValuePair<string, string> secret in secrets)
            {
                await _Db.CrawlPlans.SetSecretAsync(plan.TenantId, plan.Id, secret.Key, _Cipher.Encrypt(secret.Value), token).ConfigureAwait(false);
            }
            CrawlPlan stored = (await _Db.CrawlPlans.ReadAsync(plan.TenantId, plan.Id, token).ConfigureAwait(false))!;
            return new CrawlPlanSaveResult { Plan = stored, ChangedSecrets = secrets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList() };
        }

        /// <summary>
        /// Update a plan's configuration from <paramref name="incoming"/> (a full replacement of the configurable
        /// fields). A secret with a value replaces the stored one; a secret named in <paramref name="clearSecrets"/> is
        /// removed; other stored secrets are kept. The type cannot change.
        /// </summary>
        /// <param name="existing">The stored plan.</param>
        /// <param name="incoming">The new configuration (validated).</param>
        /// <param name="clearSecrets">Secret names to remove, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored plan and the secrets that changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="existing"/> or <paramref name="incoming"/> is null.</exception>
        public async Task<CrawlPlanSaveResult> UpdateAsync(CrawlPlan existing, CrawlPlan incoming, IEnumerable<string>? clearSecrets, CancellationToken token)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            object? settings = incoming.SettingsObject();
            Dictionary<string, string> secrets = CrawlSettingsCodec.ExtractSecrets(settings);
            CrawlSettingsCodec.ClearSecrets(settings);

            bool scheduleChanged = existing.Enabled != incoming.Enabled
                || existing.Schedule.Type != incoming.Schedule.Type
                || existing.Schedule.IntervalMinutes != incoming.Schedule.IntervalMinutes
                || !String.Equals(existing.Schedule.CronExpression, incoming.Schedule.CronExpression, StringComparison.Ordinal)
                || !String.Equals(existing.Schedule.TimeZone, incoming.Schedule.TimeZone, StringComparison.Ordinal);

            existing.Name = incoming.Name;
            existing.Enabled = incoming.Enabled;
            existing.SetSettingsObject(settings);
            existing.Filter = incoming.Filter;
            existing.Schedule = incoming.Schedule;
            existing.ProcessAdditions = incoming.ProcessAdditions;
            existing.ProcessUpdates = incoming.ProcessUpdates;
            existing.ProcessDeletions = incoming.ProcessDeletions;
            existing.MaxDeletionFraction = incoming.MaxDeletionFraction;
            existing.RetryFailedObjects = incoming.RetryFailedObjects;
            existing.Labels = incoming.Labels;
            existing.Tags = incoming.Tags;
            existing.OperationRetentionDays = incoming.OperationRetentionDays;
            if (scheduleChanged || (existing.NextRunUtc == null && existing.Enabled)) existing.NextRunUtc = CrawlScheduleCalculator.NextRunUtc(existing, DateTime.UtcNow);
            await _Db.CrawlPlans.UpdateAsync(existing, token).ConfigureAwait(false);

            List<string> changed = new List<string>();
            List<string> secretNames = CrawlSettingsCodec.SecretNames(CrawlSettingsCodec.SettingsType(existing.Type));
            foreach (KeyValuePair<string, string> secret in secrets)
            {
                await _Db.CrawlPlans.SetSecretAsync(existing.TenantId, existing.Id, secret.Key, _Cipher.Encrypt(secret.Value), token).ConfigureAwait(false);
                changed.Add(secret.Key);
            }
            if (clearSecrets != null)
            {
                foreach (string name in clearSecrets.Where(n => secretNames.Contains(n, StringComparer.Ordinal) && !secrets.ContainsKey(n)).Distinct(StringComparer.Ordinal))
                {
                    if (!existing.SecretsSet.Contains(name, StringComparer.Ordinal)) continue;
                    await _Db.CrawlPlans.DeleteSecretAsync(existing.TenantId, existing.Id, name, token).ConfigureAwait(false);
                    changed.Add(name);
                }
            }

            CrawlPlan stored = (await _Db.CrawlPlans.ReadAsync(existing.TenantId, existing.Id, token).ConfigureAwait(false))!;
            return new CrawlPlanSaveResult { Plan = stored, ChangedSecrets = changed.OrderBy(k => k, StringComparer.Ordinal).ToList() };
        }

        /// <summary>Read a plan with its secrets decrypted into its settings object, for a run, a test, or opening content.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan, or null.</returns>
        public async Task<CrawlPlan?> ReadWithSecretsAsync(string tenantId, string planId, CancellationToken token)
        {
            CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(tenantId, planId, token).ConfigureAwait(false);
            if (plan == null) return null;
            Dictionary<string, string> stored = await _Db.CrawlPlans.ReadSecretsAsync(tenantId, planId, token).ConfigureAwait(false);
            Dictionary<string, string> plain = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> secret in stored)
            {
                try
                {
                    plain[secret.Key] = _Cipher.Decrypt(secret.Value);
                }
                catch (Exception)
                {
                    // A secret encrypted under a different key cannot be used; leave it unset so the crawler reports
                    // an authentication failure instead of sending garbage.
                }
            }
            CrawlSettingsCodec.ApplySecrets(plan.SettingsObject(), plain);
            return plan;
        }

        /// <summary>
        /// Fill secrets missing from a draft plan (for example a form being edited) from the stored plan's secrets, so a
        /// test of the edited settings can use the credentials already saved.
        /// </summary>
        /// <param name="draft">The draft.</param>
        /// <param name="storedPlanId">The stored plan to take secrets from.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task FillMissingSecretsAsync(CrawlPlan draft, string storedPlanId, CancellationToken token)
        {
            if (draft == null || String.IsNullOrEmpty(storedPlanId)) return;
            CrawlPlan? stored = await ReadWithSecretsAsync(draft.TenantId, storedPlanId, token).ConfigureAwait(false);
            if (stored == null || stored.Type != draft.Type) return;
            Dictionary<string, string> have = CrawlSettingsCodec.ExtractSecrets(draft.SettingsObject());
            Dictionary<string, string> fill = CrawlSettingsCodec.ExtractSecrets(stored.SettingsObject());
            foreach (string name in have.Keys) fill.Remove(name);
            CrawlSettingsCodec.ApplySecrets(draft.SettingsObject(), fill);
        }

        #endregion

        #region Private-Methods

        private static List<string> CleanList(List<string> values)
        {
            return values
                .Select(v => (TextSanitizer.Clean(v) ?? String.Empty).Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        #endregion
    }
}
