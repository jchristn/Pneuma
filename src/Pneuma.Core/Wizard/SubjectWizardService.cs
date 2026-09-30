namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Drafts each step of the new subject wizard with a completion model: the brief, example questions, the ontology,
    /// the prompt additions, and source suggestions. Stateless: every call receives the draft so far and returns a
    /// proposal; nothing is stored. Anything the user locked or edited is kept verbatim whatever the model returns.
    /// </summary>
    public class SubjectWizardService
    {
        #region Public-Members

        /// <summary>Longest accepted description, in characters.</summary>
        public const int MaxDescriptionCharacters = 4000;

        /// <summary>Longest accepted guidance, in characters.</summary>
        public const int MaxGuidanceCharacters = 1000;

        #endregion

        #region Private-Members

        private static readonly string[] _SourceKinds = new[] { "Links", "Text", "Web", "Sitemap", "GitHub", "S3", "AzureBlob", "GoogleCloud", "Cifs", "Nfs" };
        private static readonly string[] _PromptKeys = new[] { "systemPrompt", "classifyPrompt", "rewritePrompt", "rerankingPrompt" };
        private readonly DatabaseDriverBase _Db;
        private readonly Aes256Cipher _Cipher;
        private readonly WizardGroundingReader _Grounding;
        private readonly WizardModelCaller _Caller;
        private readonly WizardSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[SubjectWizardService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cipher">Cipher for model endpoint keys.</param>
        /// <param name="http">HTTP client with the fetch-safety policy (grounding URLs); null disables grounding URLs.</param>
        /// <param name="settings">Wizard limits.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public SubjectWizardService(DatabaseDriverBase db, Aes256Cipher cipher, CrawlHttpClient? http, WizardSettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Grounding = new WizardGroundingReader(http, settings ?? throw new ArgumentNullException(nameof(settings)));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Caller = new WizardModelCaller(_Cipher, _Settings, _Logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>Draft the brief. Reads the reference URLs when given and returns the text it read.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Called as the step runs (phase, attempt, characters written, time so far); null for none.</param>
        /// <returns>The brief, or 400/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<WizardBrief>> BriefAsync(string tenantId, WizardGenerateRequest request, CancellationToken token = default, Func<WizardProgress, Task>? progress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? problem = CheckDraft(request);
            if (problem != null) return WizardResult<WizardBrief>.Fail(400, problem);

            WizardResult<WizardBrief> result = new WizardResult<WizardBrief>();
            string? grounding = Bound(request.Draft.GroundingText, _Settings.MaxGroundingCharacters);
            List<string> urls = WizardGroundingReader.UrlsOf(request.Draft);
            if (urls.Count > _Settings.MaxGroundingUrls) return WizardResult<WizardBrief>.Fail(400, "Give at most " + _Settings.MaxGroundingUrls + " reference URLs.");
            if (urls.Count > 0)
            {
                if (progress != null) await progress(new WizardProgress { Phase = "reading", Message = String.Join(", ", urls) }).ConfigureAwait(false);
                List<string> readWarnings = new List<string>();
                string? fetched = await _Grounding.ReadAsync(urls, readWarnings, token).ConfigureAwait(false);
                if (fetched == null) return WizardResult<WizardBrief>.Fail(400, "None of the reference URLs could be read: " + String.Join(" ", readWarnings));
                result.Warnings.AddRange(readWarnings);
                result.GroundingExcerpt = fetched;
                grounding = Bound(String.Join("\n\n", new[] { grounding, fetched }.Where(s => !String.IsNullOrWhiteSpace(s))), _Settings.MaxGroundingCharacters);
            }

            ModelRunner? runner = await ResolveRunnerAsync(tenantId, request.ModelRunnerId, token).ConfigureAwait(false);
            if (runner == null) return WizardResult<WizardBrief>.Fail(400, "No completion model endpoint is available. Add one under Model Endpoints first.");
            string system = await SystemPromptAsync(tenantId, SubjectWizardPrompts.BriefKey, SubjectWizardPrompts.DefaultBrief, SubjectWizardPrompts.BriefFormatKey, SubjectWizardPrompts.DefaultBriefFormat, token).ConfigureAwait(false);
            WizardBrief? brief = await CallAsync<WizardBrief, WizardBrief>("brief", runner, system, WizardMessageBuilder.Brief(request.Draft, grounding, Guidance(request)), result, progress, token).ConfigureAwait(false);
            if (brief == null) return result;

            brief.DisplayName = Clip(brief.DisplayName, 200) ?? Clip(request.Draft.Description, 60);
            brief.Type = Clip(brief.Type, 100);
            brief.Description = Clip(brief.Description, 2000);
            brief.Tagline = Clip(brief.Tagline, 200);
            brief.Audience = Clip(brief.Audience, 300);
            brief.Tone = Clip(brief.Tone, 300);
            result.Value = brief;
            return result;
        }

        /// <summary>Draft example questions: replace the unlocked ones (the default), or add more.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Called as the step runs (phase, attempt, characters written, time so far); null for none.</param>
        /// <returns>The full question list (kept questions first), or 400/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<List<WizardQuestion>>> QuestionsAsync(string tenantId, WizardGenerateRequest request, CancellationToken token = default, Func<WizardProgress, Task>? progress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? problem = CheckDraft(request);
            if (problem != null) return WizardResult<List<WizardQuestion>>.Fail(400, problem);

            bool more = String.Equals(request.Mode, "more", StringComparison.OrdinalIgnoreCase);
            List<WizardQuestion> existing = request.Draft.Questions.Where(q => q != null && !String.IsNullOrWhiteSpace(q.Question)).ToList();
            List<WizardQuestion> kept = more ? existing : existing.Where(q => q.Locked || q.Origin == SubjectQuestionOriginEnum.User).ToList();
            int target = request.Count > 0 ? request.Count : (more ? Math.Max(3, _Settings.DefaultQuestionCount / 2) : _Settings.DefaultQuestionCount);
            int wanted = more ? target : target - kept.Count;
            wanted = Math.Min(wanted, _Settings.MaxQuestions - kept.Count);

            WizardResult<List<WizardQuestion>> result = new WizardResult<List<WizardQuestion>>();
            if (wanted <= 0)
            {
                result.Value = kept;
                if (kept.Count >= _Settings.MaxQuestions) result.Warnings.Add("The draft already has the most questions allowed (" + _Settings.MaxQuestions + ").");
                return result;
            }

            ModelRunner? runner = await ResolveRunnerAsync(tenantId, request.ModelRunnerId, token).ConfigureAwait(false);
            if (runner == null) return WizardResult<List<WizardQuestion>>.Fail(400, "No completion model endpoint is available. Add one under Model Endpoints first.");
            string system = await SystemPromptAsync(tenantId, SubjectWizardPrompts.QuestionsKey, SubjectWizardPrompts.DefaultQuestions, SubjectWizardPrompts.QuestionsFormatKey, SubjectWizardPrompts.DefaultQuestionsFormat, token).ConfigureAwait(false);
            string user = WizardMessageBuilder.Questions(request.Draft, Bound(request.Draft.GroundingText, _Settings.MaxGroundingCharacters), Guidance(request), kept, wanted);
            WizardQuestionsOutput? output = await CallAsync<WizardQuestionsOutput, List<WizardQuestion>>("questions", runner, system, user, result, progress, token).ConfigureAwait(false);
            if (output == null) return result;

            List<WizardQuestion> questions = new List<WizardQuestion>(kept);
            HashSet<string> seen = new HashSet<string>(kept.Select(q => Normalize(q.Question)), StringComparer.Ordinal);
            foreach (WizardQuestionOutput item in output.Questions ?? new List<WizardQuestionOutput>())
            {
                string? text = Clip(item?.Question, 500);
                if (text == null || !seen.Add(Normalize(text))) continue;
                questions.Add(new WizardQuestion { Question = text, Kind = ParseKind(item!.Kind), Origin = SubjectQuestionOriginEnum.Model });
                if (questions.Count - kept.Count >= wanted) break;
            }
            if (questions.Count == kept.Count) result.Warnings.Add("The model proposed no new questions.");
            result.Value = questions;
            return result;
        }

        /// <summary>Draft the ontology from the brief and questions, keeping the types the user locked.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Called as the step runs (phase, attempt, characters written, time so far); null for none.</param>
        /// <returns>The normalized ontology, or 400/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<WizardOntology>> OntologyAsync(string tenantId, WizardGenerateRequest request, CancellationToken token = default, Func<WizardProgress, Task>? progress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? problem = CheckDraft(request);
            if (problem != null) return WizardResult<WizardOntology>.Fail(400, problem);
            int questionCount = request.Draft.Questions.Count(q => q != null && !String.IsNullOrWhiteSpace(q.Question));
            if (questionCount == 0) return WizardResult<WizardOntology>.Fail(400, "Add example questions before drafting the ontology.");

            WizardOntology kept = new WizardOntology();
            if (request.Draft.Ontology != null)
            {
                kept.NodeTypes = request.Draft.Ontology.NodeTypes.Where(n => n != null && n.Locked).ToList();
                kept.EdgeTypes = request.Draft.Ontology.EdgeTypes.Where(e => e != null && e.Locked).ToList();
            }

            ModelRunner? runner = await ResolveRunnerAsync(tenantId, request.ModelRunnerId, token).ConfigureAwait(false);
            if (runner == null) return WizardResult<WizardOntology>.Fail(400, "No completion model endpoint is available. Add one under Model Endpoints first.");
            WizardResult<WizardOntology> result = new WizardResult<WizardOntology>();
            string system = await SystemPromptAsync(tenantId, SubjectWizardPrompts.OntologyKey, SubjectWizardPrompts.DefaultOntology, SubjectWizardPrompts.OntologyFormatKey, SubjectWizardPrompts.DefaultOntologyFormat, token).ConfigureAwait(false);
            string user = WizardMessageBuilder.Ontology(request.Draft, Guidance(request), WizardOntologyBuilder.FromTemplate(), kept);
            WizardOntologyOutput? output = await CallAsync<WizardOntologyOutput, WizardOntology>("ontology", runner, system, user, result, progress, token).ConfigureAwait(false);
            if (output == null) return result;

            WizardOntology ontology = new WizardOntology { NodeTypes = new List<WizardNodeType>(kept.NodeTypes), EdgeTypes = new List<WizardEdgeType>(kept.EdgeTypes) };
            foreach (WizardTypeOutput node in output.NodeTypes ?? new List<WizardTypeOutput>())
            {
                if (node == null || String.IsNullOrWhiteSpace(node.Name)) continue;
                string name = WizardOntologyBuilder.ToPascal(node.Name);
                if (ontology.NodeTypes.Any(n => String.Equals(WizardOntologyBuilder.ToPascal(n.Name), name, StringComparison.OrdinalIgnoreCase))) continue;
                ontology.NodeTypes.Add(new WizardNodeType { Name = name, Description = node.Description, Questions = node.Questions ?? new List<int>() });
            }
            HashSet<string> lockedEdges = new HashSet<string>(kept.EdgeTypes.Select(e => (e.Name ?? String.Empty).Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (WizardTypeOutput edge in output.EdgeTypes ?? new List<WizardTypeOutput>())
            {
                if (edge == null || String.IsNullOrWhiteSpace(edge.Name) || lockedEdges.Contains(edge.Name.Trim())) continue;
                ontology.EdgeTypes.Add(new WizardEdgeType { Name = edge.Name, Description = edge.Description, From = edge.From, To = edge.To, Questions = edge.Questions ?? new List<int>() });
            }
            ontology.Guidance = String.IsNullOrWhiteSpace(output.Guidance) ? request.Draft.Ontology?.Guidance : output.Guidance;
            result.Value = WizardOntologyBuilder.Normalize(ontology, questionCount, _Settings.MaxOntologyTypes, result.Warnings);
            return result;
        }

        /// <summary>Draft the four prompt additions, keeping the ones the user locked.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Called as the step runs (phase, attempt, characters written, time so far); null for none.</param>
        /// <returns>The prompts, or 400/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<WizardPrompts>> PromptsAsync(string tenantId, WizardGenerateRequest request, CancellationToken token = default, Func<WizardProgress, Task>? progress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? problem = CheckDraft(request);
            if (problem != null) return WizardResult<WizardPrompts>.Fail(400, problem);

            WizardPrompts current = request.Draft.Prompts ?? new WizardPrompts();
            Dictionary<string, string> kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in current.Locked.Where(k => _PromptKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
                kept[key] = PromptValue(current, key) ?? String.Empty;

            WizardResult<WizardPrompts> result = new WizardResult<WizardPrompts>();
            ModelRunner? runner = await ResolveRunnerAsync(tenantId, request.ModelRunnerId, token).ConfigureAwait(false);
            if (runner == null) return WizardResult<WizardPrompts>.Fail(400, "No completion model endpoint is available. Add one under Model Endpoints first.");
            string system = await SystemPromptAsync(tenantId, SubjectWizardPrompts.PromptsKey, SubjectWizardPrompts.DefaultPrompts, SubjectWizardPrompts.PromptsFormatKey, SubjectWizardPrompts.DefaultPromptsFormat, token).ConfigureAwait(false);
            WizardPrompts? output = await CallAsync<WizardPrompts, WizardPrompts>("prompts", runner, system, WizardMessageBuilder.Prompts(request.Draft, Guidance(request), kept), result, progress, token).ConfigureAwait(false);
            if (output == null) return result;

            WizardPrompts prompts = new WizardPrompts
            {
                SystemPrompt = Clip(output.SystemPrompt, _Settings.MaxPromptCharacters),
                ClassifyPrompt = Clip(output.ClassifyPrompt, _Settings.MaxPromptCharacters),
                RewritePrompt = Clip(output.RewritePrompt, _Settings.MaxPromptCharacters),
                RerankingPrompt = Clip(output.RerankingPrompt, _Settings.MaxPromptCharacters),
                Locked = kept.Keys.ToList()
            };
            foreach (KeyValuePair<string, string> pair in kept) SetPromptValue(prompts, pair.Key, pair.Value);
            result.Value = prompts;
            return result;
        }

        /// <summary>Suggest where content for the subject might come from.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Called as the step runs (phase, attempt, characters written, time so far); null for none.</param>
        /// <returns>The suggestions, or 400/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<List<WizardSourceSuggestion>>> SourcesAsync(string tenantId, WizardGenerateRequest request, CancellationToken token = default, Func<WizardProgress, Task>? progress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? problem = CheckDraft(request);
            if (problem != null) return WizardResult<List<WizardSourceSuggestion>>.Fail(400, problem);

            ModelRunner? runner = await ResolveRunnerAsync(tenantId, request.ModelRunnerId, token).ConfigureAwait(false);
            if (runner == null) return WizardResult<List<WizardSourceSuggestion>>.Fail(400, "No completion model endpoint is available. Add one under Model Endpoints first.");
            WizardResult<List<WizardSourceSuggestion>> result = new WizardResult<List<WizardSourceSuggestion>>();
            string system = await SystemPromptAsync(tenantId, SubjectWizardPrompts.SourcesKey, SubjectWizardPrompts.DefaultSources, SubjectWizardPrompts.SourcesFormatKey, SubjectWizardPrompts.DefaultSourcesFormat, token).ConfigureAwait(false);
            WizardSourcesOutput? output = await CallAsync<WizardSourcesOutput, List<WizardSourceSuggestion>>("sources", runner, system, WizardMessageBuilder.Sources(request.Draft, Guidance(request)), result, progress, token).ConfigureAwait(false);
            if (output == null) return result;

            List<WizardSourceSuggestion> suggestions = new List<WizardSourceSuggestion>();
            foreach (WizardSourceSuggestion item in output.Suggestions ?? new List<WizardSourceSuggestion>())
            {
                string? title = Clip(item?.Title, 200);
                if (title == null) continue;
                string kind = _SourceKinds.FirstOrDefault(k => String.Equals(k, item!.Kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Links";
                suggestions.Add(new WizardSourceSuggestion { Kind = kind, Title = title, Detail = Clip(item!.Detail, 600) });
                if (suggestions.Count >= 8) break;
            }
            result.Value = suggestions;
            return result;
        }

        #endregion

        #region Private-Methods

        private string? CheckDraft(WizardGenerateRequest request)
        {
            string description = request.Draft.Description ?? String.Empty;
            if (String.IsNullOrWhiteSpace(description)) return "Describe the subject first: what it is, and who will ask about it.";
            if (description.Length > MaxDescriptionCharacters) return "The description is too long (" + MaxDescriptionCharacters + " characters at most).";
            if (request.Guidance != null && request.Guidance.Length > MaxGuidanceCharacters) return "The guidance is too long (" + MaxGuidanceCharacters + " characters at most).";
            if (request.Draft.Questions.Count > _Settings.MaxQuestions) return "The draft has too many questions (" + _Settings.MaxQuestions + " at most).";
            return null;
        }

        private async Task<ModelRunner?> ResolveRunnerAsync(string tenantId, string? runnerId, CancellationToken token)
        {
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            List<ModelRunner> usable = runners.Where(r => r.Active && r.Capabilities.Contains(ModelCapabilityEnum.Completion)).ToList();
            if (!String.IsNullOrWhiteSpace(runnerId)) return usable.FirstOrDefault(r => r.Id == runnerId);
            return usable.FirstOrDefault();
        }

        private async Task<string> SystemPromptAsync(string tenantId, string taskKey, string taskDefault, string formatKey, string formatDefault, CancellationToken token)
        {
            PromptResolver resolver = new PromptResolver(_Db);
            ResolvedPrompt task = await resolver.ResolveAsync(tenantId, null, taskKey, null, token).ConfigureAwait(false);
            ResolvedPrompt format = await resolver.ResolveAsync(tenantId, null, formatKey, null, token).ConfigureAwait(false);
            return (String.IsNullOrWhiteSpace(task.EffectiveContent) ? taskDefault : task.EffectiveContent) + "\n\n" +
                (String.IsNullOrWhiteSpace(format.EffectiveContent) ? formatDefault : format.EffectiveContent);
        }

        private async Task<TParsed?> CallAsync<TParsed, TOut>(string step, ModelRunner runner, string system, string user, WizardResult<TOut> result, Func<WizardProgress, Task>? progress, CancellationToken token)
            where TParsed : class
            where TOut : class
        {
            result.ModelRunnerId = runner.Id;
            result.Model = String.IsNullOrWhiteSpace(runner.DefaultModel) ? runner.Name : runner.DefaultModel;
            Stopwatch watch = Stopwatch.StartNew();
            string prompt = user;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                string? parseError;
                try
                {
                    await ReportAsync(progress, "waiting", attempt, watch, null).ConfigureAwait(false);
                    string text = await _Caller.CompleteAsync(runner, system, prompt, attempt, watch, progress, token).ConfigureAwait(false);
                    await ReportAsync(progress, "checking", attempt, watch, null).ConfigureAwait(false);
                    TParsed? parsed = Parse<TParsed>(text, out parseError);
                    if (parsed != null)
                    {
                        // Clear what a failed first attempt recorded.
                        result.StatusCode = 200;
                        result.Error = null;
                        result.ElapsedMs = watch.ElapsedMilliseconds;
                        WizardMetrics.RecordGeneration(step, attempt == 1 ? "success" : "retried", watch.Elapsed.TotalSeconds);
                        return parsed;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    parseError = null;
                    result.StatusCode = 504;
                    result.Error = "The model did not finish in time (nothing new for " + _Settings.TimeoutSeconds + " seconds, or more than " + (_Settings.TimeoutSeconds * 3) + " seconds in all). Try a faster model or regenerate.";
                    break;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    result.StatusCode = 502;
                    result.Error = "The model call failed: " + e.Message;
                    _Logging.Warn(_Header + step + " failed: " + e.Message);
                    break;
                }
                result.StatusCode = 502;
                result.Error = "The model did not return the expected JSON: " + parseError;
                prompt = user + "\n\nYour previous reply could not be used (" + parseError + "). Reply again with only the JSON object in the required shape.";
                if (attempt == 1) await ReportAsync(progress, "retrying", attempt + 1, watch, "The reply could not be used (" + parseError + "); asking again.").ConfigureAwait(false);
            }
            result.ElapsedMs = watch.ElapsedMilliseconds;
            WizardMetrics.RecordGeneration(step, "failed", watch.Elapsed.TotalSeconds);
            return null;
        }

        private static async Task ReportAsync(Func<WizardProgress, Task>? progress, string phase, int attempt, Stopwatch watch, string? message)
        {
            if (progress == null) return;
            await progress(new WizardProgress { Phase = phase, Attempt = attempt, ElapsedMs = watch.ElapsedMilliseconds, Message = message }).ConfigureAwait(false);
        }

        private static T? Parse<T>(string text, out string? error) where T : class
        {
            error = null;
            string trimmed = (text ?? String.Empty).Trim();
            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "the reply contained no JSON object";
                return null;
            }
            try
            {
                T? value = Json.Deserialize<T>(trimmed.Substring(start, end - start + 1));
                if (value == null) error = "the JSON object was empty";
                return value;
            }
            catch (System.Text.Json.JsonException e)
            {
                error = e.Message;
                return null;
            }
        }

        private static string? Guidance(WizardGenerateRequest request)
        {
            return String.IsNullOrWhiteSpace(request.Guidance) ? null : request.Guidance.Trim();
        }

        private static string? PromptValue(WizardPrompts prompts, string key)
        {
            if (String.Equals(key, "systemPrompt", StringComparison.OrdinalIgnoreCase)) return prompts.SystemPrompt;
            if (String.Equals(key, "classifyPrompt", StringComparison.OrdinalIgnoreCase)) return prompts.ClassifyPrompt;
            if (String.Equals(key, "rewritePrompt", StringComparison.OrdinalIgnoreCase)) return prompts.RewritePrompt;
            if (String.Equals(key, "rerankingPrompt", StringComparison.OrdinalIgnoreCase)) return prompts.RerankingPrompt;
            return null;
        }

        private static void SetPromptValue(WizardPrompts prompts, string key, string value)
        {
            string? content = String.IsNullOrWhiteSpace(value) ? null : value;
            if (String.Equals(key, "systemPrompt", StringComparison.OrdinalIgnoreCase)) prompts.SystemPrompt = content;
            else if (String.Equals(key, "classifyPrompt", StringComparison.OrdinalIgnoreCase)) prompts.ClassifyPrompt = content;
            else if (String.Equals(key, "rewritePrompt", StringComparison.OrdinalIgnoreCase)) prompts.RewritePrompt = content;
            else if (String.Equals(key, "rerankingPrompt", StringComparison.OrdinalIgnoreCase)) prompts.RerankingPrompt = content;
        }

        private static SubjectQuestionKindEnum ParseKind(string? kind)
        {
            SubjectQuestionKindEnum parsed;
            if (!String.IsNullOrWhiteSpace(kind) && Enum.TryParse(kind.Trim(), true, out parsed) && Enum.IsDefined(typeof(SubjectQuestionKindEnum), parsed)) return parsed;
            return SubjectQuestionKindEnum.Fact;
        }

        private static string Normalize(string text)
        {
            return new string((text ?? String.Empty).ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray());
        }

        private static string? Bound(string? text, int max)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text.Trim();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }

        private static string? Clip(string? text, int max)
        {
            return Bound(text, max);
        }

        #endregion
    }
}
