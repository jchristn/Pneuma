namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Converts a crawl plan's typed settings to and from their stored form, describes them as a form schema, and
    /// validates them, all from the <see cref="CrawlSettingAttribute"/> on each settings property. Supported property
    /// types are string, bool, int, long, and <c>List&lt;string&gt;</c>. Secrets are never part of the rows; callers
    /// pull them out with <see cref="ExtractSecrets"/> and store them encrypted.
    /// </summary>
    public static class CrawlSettingsCodec
    {
        #region Public-Members

        /// <summary>Row name prefix for filter lists (for example "filter.includePatterns").</summary>
        public const string FilterPrefix = "filter.";

        /// <summary>Row name for plan labels (one row per label).</summary>
        public const string LabelRow = "plan.label";

        /// <summary>Row name prefix for plan tags (the rest of the name is the tag key).</summary>
        public const string TagPrefix = "plan.tag.";

        #endregion

        #region Public-Methods

        /// <summary>The settings class for a crawler type.</summary>
        /// <param name="type">Crawler type.</param>
        /// <returns>The settings class.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an unknown type.</exception>
        public static Type SettingsType(CrawlPlanTypeEnum type)
        {
            switch (type)
            {
                case CrawlPlanTypeEnum.Web: return typeof(WebCrawlSettings);
                case CrawlPlanTypeEnum.Sitemap: return typeof(SitemapCrawlSettings);
                case CrawlPlanTypeEnum.S3: return typeof(S3CrawlSettings);
                case CrawlPlanTypeEnum.Cifs: return typeof(CifsCrawlSettings);
                case CrawlPlanTypeEnum.Nfs: return typeof(NfsCrawlSettings);
                case CrawlPlanTypeEnum.GitHub: return typeof(GitHubCrawlSettings);
                case CrawlPlanTypeEnum.AzureBlob: return typeof(AzureBlobCrawlSettings);
                case CrawlPlanTypeEnum.GoogleCloud: return typeof(GoogleCloudCrawlSettings);
                case CrawlPlanTypeEnum.LocalFolder: return typeof(LocalFolderCrawlSettings);
                default: throw new ArgumentOutOfRangeException(nameof(type), "Unknown crawl plan type " + type + ".");
            }
        }

        /// <summary>The JSON property of a crawl plan that holds a type's settings (for example "web").</summary>
        /// <param name="type">Crawler type.</param>
        /// <returns>The property name in camelCase.</returns>
        public static string SettingsProperty(CrawlPlanTypeEnum type)
        {
            return CamelCase(type.ToString());
        }

        /// <summary>Describe a settings class as form fields, in declaration order.</summary>
        /// <param name="settingsType">The settings class.</param>
        /// <returns>The fields.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the type is null.</exception>
        public static List<CrawlSettingField> Describe(Type settingsType)
        {
            if (settingsType == null) throw new ArgumentNullException(nameof(settingsType));
            object defaults = Activator.CreateInstance(settingsType)!;
            List<CrawlSettingField> fields = new List<CrawlSettingField>();
            foreach (PropertyInfo prop in SettingProperties(settingsType))
            {
                CrawlSettingAttribute attr = prop.GetCustomAttribute<CrawlSettingAttribute>()!;
                CrawlSettingField field = new CrawlSettingField
                {
                    Name = CamelCase(prop.Name),
                    Label = attr.Label,
                    Help = attr.Help,
                    Kind = KindOf(prop, attr),
                    Required = attr.Required
                };
                if (attr.Max > attr.Min)
                {
                    field.Min = attr.Min;
                    field.Max = attr.Max;
                }
                if (attr.Options != null) field.Options = attr.Options.ToList();
                if (!attr.Secret) field.Default = FormatDefault(prop.GetValue(defaults));
                fields.Add(field);
            }
            return fields;
        }

        /// <summary>The names (camelCase) of a settings class's secret properties.</summary>
        /// <param name="settingsType">The settings class.</param>
        /// <returns>The secret names.</returns>
        public static List<string> SecretNames(Type settingsType)
        {
            if (settingsType == null) throw new ArgumentNullException(nameof(settingsType));
            return SettingProperties(settingsType)
                .Where(p => p.GetCustomAttribute<CrawlSettingAttribute>()!.Secret)
                .Select(p => CamelCase(p.Name))
                .ToList();
        }

        /// <summary>
        /// The stored rows for a plan: its type's non-secret settings, its filter lists, its labels, and its tags.
        /// Scalar filter and schedule values are plan columns and are not included.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The rows.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the plan is null.</exception>
        public static List<CrawlPlanSetting> ToRows(CrawlPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            List<CrawlPlanSetting> rows = new List<CrawlPlanSetting>();
            object? settings = plan.SettingsObject();
            if (settings != null)
            {
                foreach (PropertyInfo prop in SettingProperties(settings.GetType()))
                {
                    if (prop.GetCustomAttribute<CrawlSettingAttribute>()!.Secret) continue;
                    AddRows(rows, CamelCase(prop.Name), prop.GetValue(settings));
                }
            }
            AddList(rows, FilterPrefix + "includePatterns", plan.Filter.IncludePatterns);
            AddList(rows, FilterPrefix + "excludePatterns", plan.Filter.ExcludePatterns);
            AddList(rows, FilterPrefix + "allowedContentTypes", plan.Filter.AllowedContentTypes);
            AddList(rows, LabelRow, plan.Labels);
            foreach (KeyValuePair<string, string> tag in plan.Tags.OrderBy(t => t.Key, StringComparer.Ordinal))
                rows.Add(new CrawlPlanSetting { Name = TagPrefix + tag.Key, Ordinal = 0, Value = tag.Value ?? String.Empty });
            return rows;
        }

        /// <summary>
        /// Apply stored rows to a plan whose <see cref="CrawlPlan.Type"/> is set: builds its settings object (defaults
        /// for missing rows), and fills its filter lists, labels, and tags. Unknown names are ignored.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The rows.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void ApplyRows(CrawlPlan plan, IEnumerable<CrawlPlanSetting> rows)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<CrawlPlanSetting> ordered = rows.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Ordinal).ToList();

            Type settingsType = SettingsType(plan.Type);
            object settings = Activator.CreateInstance(settingsType)!;
            foreach (PropertyInfo prop in SettingProperties(settingsType))
            {
                if (prop.GetCustomAttribute<CrawlSettingAttribute>()!.Secret) continue;
                string name = CamelCase(prop.Name);
                List<string> values = ordered.Where(r => r.Name == name).Select(r => r.Value).ToList();
                if (prop.PropertyType == typeof(List<string>))
                {
                    // A list with no rows keeps its default only when it was never stored; an explicit empty list is
                    // stored as a single row with ordinal -1.
                    if (values.Count == 0) continue;
                    prop.SetValue(settings, ordered.Where(r => r.Name == name && r.Ordinal >= 0).Select(r => r.Value).ToList());
                    continue;
                }
                if (values.Count == 0) continue;
                object? parsed = ParseScalar(prop.PropertyType, values[0]);
                if (parsed != null || IsNullable(prop)) prop.SetValue(settings, parsed);
            }
            plan.SetSettingsObject(settings);

            plan.Filter.IncludePatterns = ListOf(ordered, FilterPrefix + "includePatterns");
            plan.Filter.ExcludePatterns = ListOf(ordered, FilterPrefix + "excludePatterns");
            plan.Filter.AllowedContentTypes = ListOf(ordered, FilterPrefix + "allowedContentTypes");
            plan.Labels = ListOf(ordered, LabelRow);
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CrawlPlanSetting row in ordered.Where(r => r.Name.StartsWith(TagPrefix, StringComparison.Ordinal)))
                tags[row.Name.Substring(TagPrefix.Length)] = row.Value;
            plan.Tags = tags;
        }

        /// <summary>The non-empty secret values of a settings object, by camelCase name.</summary>
        /// <param name="settings">The settings object, or null.</param>
        /// <returns>The secrets (plaintext).</returns>
        public static Dictionary<string, string> ExtractSecrets(object? settings)
        {
            Dictionary<string, string> secrets = new Dictionary<string, string>(StringComparer.Ordinal);
            if (settings == null) return secrets;
            foreach (PropertyInfo prop in SettingProperties(settings.GetType()))
            {
                if (!prop.GetCustomAttribute<CrawlSettingAttribute>()!.Secret) continue;
                string? value = prop.GetValue(settings) as string;
                if (!String.IsNullOrEmpty(value)) secrets[CamelCase(prop.Name)] = value;
            }
            return secrets;
        }

        /// <summary>Set every secret property of a settings object to null (before it is returned to a caller).</summary>
        /// <param name="settings">The settings object, or null.</param>
        public static void ClearSecrets(object? settings)
        {
            if (settings == null) return;
            foreach (PropertyInfo prop in SettingProperties(settings.GetType()))
            {
                if (prop.GetCustomAttribute<CrawlSettingAttribute>()!.Secret) prop.SetValue(settings, null);
            }
        }

        /// <summary>Set secret properties of a settings object from plaintext values by camelCase name.</summary>
        /// <param name="settings">The settings object, or null.</param>
        /// <param name="secrets">The secrets.</param>
        public static void ApplySecrets(object? settings, Dictionary<string, string> secrets)
        {
            if (settings == null || secrets == null) return;
            foreach (PropertyInfo prop in SettingProperties(settings.GetType()))
            {
                if (!prop.GetCustomAttribute<CrawlSettingAttribute>()!.Secret) continue;
                string? value;
                if (secrets.TryGetValue(CamelCase(prop.Name), out value)) prop.SetValue(settings, value);
            }
        }

        /// <summary>
        /// Validate a settings object against its attributes: required values, numeric ranges, and choice options.
        /// Secrets named in <paramref name="storedSecrets"/> count as present.
        /// </summary>
        /// <param name="settings">The settings object.</param>
        /// <param name="storedSecrets">Names of secrets already stored for the plan, or null.</param>
        /// <returns>The problems found; empty when the settings are valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the settings are null.</exception>
        public static List<string> Validate(object settings, ICollection<string>? storedSecrets = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            List<string> errors = new List<string>();
            foreach (PropertyInfo prop in SettingProperties(settings.GetType()))
            {
                CrawlSettingAttribute attr = prop.GetCustomAttribute<CrawlSettingAttribute>()!;
                string name = CamelCase(prop.Name);
                object? value = prop.GetValue(settings);

                if (attr.Required)
                {
                    bool missing;
                    if (value is List<string> list) missing = !list.Any(v => !String.IsNullOrWhiteSpace(v));
                    else if (value is string s) missing = String.IsNullOrWhiteSpace(s);
                    else missing = value == null;
                    if (missing && attr.Secret && storedSecrets != null && storedSecrets.Contains(name)) missing = false;
                    if (missing) errors.Add(name + " is required.");
                }

                if (attr.Max > attr.Min && (value is int || value is long))
                {
                    long number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    if (number < attr.Min || number > attr.Max)
                        errors.Add(name + " must be between " + attr.Min.ToString(CultureInfo.InvariantCulture) + " and " + attr.Max.ToString(CultureInfo.InvariantCulture) + ".");
                }

                if (attr.Options != null && value is string choice && !String.IsNullOrEmpty(choice))
                {
                    if (!attr.Options.Contains(choice, StringComparer.Ordinal))
                        errors.Add(name + " must be one of " + String.Join(", ", attr.Options) + ".");
                }
            }
            return errors;
        }

        /// <summary>Convert a PascalCase name to camelCase.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The camelCase name.</returns>
        public static string CamelCase(string name)
        {
            if (String.IsNullOrEmpty(name)) return String.Empty;
            // Leading acronyms lower as a block ("S3" stays "s3", "Nfs" becomes "nfs").
            int upper = 0;
            while (upper < name.Length && !Char.IsLower(name[upper])) upper++;
            if (upper <= 1 || upper == name.Length) return upper == name.Length ? name.ToLowerInvariant() : Char.ToLowerInvariant(name[0]) + name.Substring(1);
            return name.Substring(0, upper - 1).ToLowerInvariant() + name.Substring(upper - 1);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<PropertyInfo> SettingProperties(Type settingsType)
        {
            return settingsType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<CrawlSettingAttribute>() != null)
                .OrderBy(p => p.MetadataToken);
        }

        private static string KindOf(PropertyInfo prop, CrawlSettingAttribute attr)
        {
            if (attr.Secret) return "secret";
            if (attr.Options != null) return "choice";
            Type type = prop.PropertyType;
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(List<string>)) return "list";
            return "string";
        }

        private static string? FormatDefault(object? value)
        {
            if (value == null) return null;
            if (value is List<string> list) return String.Join("\n", list);
            if (value is bool b) return b ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static void AddRows(List<CrawlPlanSetting> rows, string name, object? value)
        {
            if (value == null) return;
            if (value is List<string> list)
            {
                AddList(rows, name, list);
                return;
            }
            string text;
            if (value is bool b) text = b ? "true" : "false";
            else text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            rows.Add(new CrawlPlanSetting { Name = name, Ordinal = 0, Value = text });
        }

        private static void AddList(List<CrawlPlanSetting> rows, string name, List<string> list)
        {
            List<string> values = list.Where(v => v != null).ToList();
            if (values.Count == 0)
            {
                // Record the empty list so it is not replaced by the class default on read.
                rows.Add(new CrawlPlanSetting { Name = name, Ordinal = -1, Value = String.Empty });
                return;
            }
            for (int i = 0; i < values.Count; i++)
                rows.Add(new CrawlPlanSetting { Name = name, Ordinal = i, Value = values[i] });
        }

        private static List<string> ListOf(List<CrawlPlanSetting> ordered, string name)
        {
            return ordered.Where(r => r.Name == name && r.Ordinal >= 0).Select(r => r.Value).ToList();
        }

        private static object? ParseScalar(Type type, string text)
        {
            if (type == typeof(string)) return text;
            if (type == typeof(bool))
            {
                bool b;
                return Boolean.TryParse(text, out b) ? b : null;
            }
            if (type == typeof(int))
            {
                int i;
                return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : null;
            }
            if (type == typeof(long))
            {
                long l;
                return Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) ? l : null;
            }
            return null;
        }

        private static bool IsNullable(PropertyInfo prop)
        {
            if (!prop.PropertyType.IsValueType) return new NullabilityInfoContext().Create(prop).WriteState == NullabilityState.Nullable;
            return Nullable.GetUnderlyingType(prop.PropertyType) != null;
        }

        #endregion
    }
}
