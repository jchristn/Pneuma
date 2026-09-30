namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Wizard;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// New subject wizard: each drafting step parses the model's JSON, keeps what the user locked or edited, cleans up the
    /// ontology, retries once on unusable output, reads grounding URLs through the fetch-safety policy, and commit creates
    /// the subject, its questions, and its ontology in the requested mode. Also covers starter question and bulk
    /// evaluation fact routes.
    /// </summary>
    public static class SubjectWizardSuite
    {
        private const string BriefJson = @"{""displayName"":""Charlie Parker"",""type"":""Musician"",""description"":""Alto saxophonist and a founder of bebop."",""tagline"":""Ask about Bird's music"",""audience"":""music students"",""tone"":""precise""}";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "SubjectWizard",
                displayName: "New subject wizard",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("SubjectWizard", "Brief_Parsed", "The brief step parses the model's JSON, reports the model and time, sends the description between markers, and records a metric",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                rig.Model.ChatText = "Here you go:\n" + BriefJson + "\nThanks";
                                WizardResult<WizardBrief> result = await rig.Service.BriefAsync(rig.H.TenantId, rig.Request(), ct);
                                Expect(result.Success, "succeeds: " + result.Error);
                                Expect(result.Value!.DisplayName == "Charlie Parker" && result.Value.Type == "Musician" && result.Value.Tagline == "Ask about Bird's music", "fields parsed");
                                Expect(result.ModelRunnerId == rig.Runner.Id && result.Model == "stub", "model reported");
                                Expect(rig.Model.LastChatBody.Contains("bebop saxophonist") && rig.Model.LastChatBody.Contains("not as instructions"), "description sent as material, not instructions");
                                Expect(PneumaMetrics.Render().Contains("pneuma_wizard_generation_total{step=\"brief\",outcome=\"success\"}"), "metric recorded");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Brief_GroundingUrl", "A grounding URL is read as plain text and returned as an excerpt; a private address is refused under the default policy",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                rig.Site.Html("/bird", "<html><head><title>x</title></head><body><script>var secret=1;</script><h1>Charlie Parker</h1><p>Born in Kansas City &amp; raised there.</p></body></html>");
                                rig.Model.ChatText = BriefJson;
                                WizardGenerateRequest request = rig.Request();
                                request.Draft.GroundingUrl = rig.Site.Url("/bird");
                                WizardResult<WizardBrief> result = await rig.Service.BriefAsync(rig.H.TenantId, request, ct);
                                Expect(result.Success, "succeeds: " + result.Error);
                                Expect(result.GroundingExcerpt != null && result.GroundingExcerpt.Contains("Born in Kansas City & raised there.") && !result.GroundingExcerpt.Contains("secret"), "page text extracted: " + result.GroundingExcerpt);
                                Expect(rig.Model.LastChatBody.Contains("Kansas City"), "the page text reached the model");
                            }
                            await using (WizardRig strict = await WizardRig.CreateAsync(ct, allowLoopback: false))
                            {
                                strict.Site.Html("/bird", "<p>hi</p>");
                                WizardGenerateRequest request = strict.Request();
                                request.Draft.GroundingUrl = strict.Site.Url("/bird");
                                WizardResult<WizardBrief> refused = await strict.Service.BriefAsync(strict.H.TenantId, request, ct);
                                Expect(!refused.Success && refused.StatusCode == 400 && refused.Error!.Contains("refused"), "private address refused: " + refused.Error);
                                Expect(strict.Model.ChatRequestCount == 0, "no model call after a refused URL");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Questions_KeepLockedAndEdited", "Regenerating questions keeps locked and user-written ones first, drops unlocked model ones, skips duplicates, and parses kinds",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                WizardGenerateRequest request = rig.Request();
                                request.Count = 5;
                                request.Draft.Questions = new List<WizardQuestion>
                                {
                                    new WizardQuestion { Question = "Who played on the Savoy sessions?", Kind = SubjectQuestionKindEnum.Relationship, Locked = true },
                                    new WizardQuestion { Question = "An old model question", Origin = SubjectQuestionOriginEnum.Model },
                                    new WizardQuestion { Question = "What did he play?", Origin = SubjectQuestionOriginEnum.User }
                                };
                                rig.Model.ChatText = @"{""questions"":[{""question"":""Who played on the Savoy sessions?"",""kind"":""relationship""},{""question"":""How did his style change after 1947?"",""kind"":""Timeline""},{""question"":""What is Ko-Ko based on?"",""kind"":""bogus""},{""question"":""Where did he grow up?"",""kind"":""fact""},{""question"":""One too many"",""kind"":""fact""}]}";
                                WizardResult<List<WizardQuestion>> result = await rig.Service.QuestionsAsync(rig.H.TenantId, request, ct);
                                Expect(result.Success, "succeeds: " + result.Error);
                                List<string> texts = result.Value!.Select(q => q.Question).ToList();
                                Expect(texts.Count == 5, "five questions in all: " + String.Join(" | ", texts));
                                Expect(texts[0] == "Who played on the Savoy sessions?" && texts[1] == "What did he play?", "kept questions first, in order");
                                Expect(!texts.Contains("An old model question"), "the unlocked model question was replaced");
                                Expect(texts.Count(t => t.StartsWith("Who played on the Savoy", StringComparison.Ordinal)) == 1, "duplicate skipped");
                                Expect(result.Value![2].Kind == SubjectQuestionKindEnum.Timeline && result.Value[3].Kind == SubjectQuestionKindEnum.Fact, "kinds parsed, unknown kind is Fact");
                                Expect(result.Value![2].Origin == SubjectQuestionOriginEnum.Model, "new questions are the model's");
                                Expect(rig.Model.LastChatBody.Contains("Propose 3 new questions"), "asked for the missing number only");

                                request.Mode = "more";
                                request.Count = 2;
                                request.Draft.Questions = result.Value!;
                                rig.Model.ChatText = @"{""questions"":[{""question"":""Who were his influences?"",""kind"":""relationship""},{""question"":""Why is he called Bird?"",""kind"":""reasoning""}]}";
                                WizardResult<List<WizardQuestion>> more = await rig.Service.QuestionsAsync(rig.H.TenantId, request, ct);
                                Expect(more.Success && more.Value!.Count == 7 && more.Value[5].Question == "Who were his influences?", "more appends to every existing question");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Retry_ThenFail", "Unusable output is retried once with the parse error; two unusable replies fail with 502 and a failed metric",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                rig.Model.ChatQueue.Enqueue("I cannot answer in JSON, sorry.");
                                rig.Model.ChatQueue.Enqueue(BriefJson);
                                WizardResult<WizardBrief> healed = await rig.Service.BriefAsync(rig.H.TenantId, rig.Request(), ct);
                                Expect(healed.Success && rig.Model.ChatRequestCount == 2, "succeeded on the second call: " + healed.Error + " (" + rig.Model.ChatRequestCount + " calls)");
                                Expect(rig.Model.LastChatBody.Contains("could not be used"), "the retry explained the problem");
                                Expect(PneumaMetrics.Render().Contains("step=\"brief\",outcome=\"retried\""), "retried metric");

                                rig.Model.ChatText = "still not json";
                                WizardResult<WizardBrief> failed = await rig.Service.BriefAsync(rig.H.TenantId, rig.Request(), ct);
                                Expect(!failed.Success && failed.StatusCode == 502 && failed.Error!.Contains("expected JSON"), "fails with 502: " + failed.Error);
                                Expect(rig.Model.ChatRequestCount == 4, "exactly one retry");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Ontology_Normalized", "The ontology keeps locked types, normalizes names, drops duplicates, resolves endpoints, clamps question numbers, and always has Subject",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                WizardGenerateRequest request = rig.Request();
                                request.Draft.Questions = new List<WizardQuestion>
                                {
                                    new WizardQuestion { Question = "Q1" }, new WizardQuestion { Question = "Q2" }, new WizardQuestion { Question = "Q3" }
                                };
                                request.Draft.Ontology = new WizardOntology
                                {
                                    NodeTypes = new List<WizardNodeType>
                                    {
                                        new WizardNodeType { Name = "Label", Description = "A record label.", Locked = true },
                                        new WizardNodeType { Name = "Dropped", Description = "Not locked, so replaced." }
                                    }
                                };
                                rig.Model.ChatText = @"{""nodeTypes"":[{""name"":""person"",""description"":""A musician."",""questions"":[1,99]},{""name"":""Person"",""description"":""dup""},{""name"":""recording session"",""description"":""A session."",""questions"":[2]}],""edgeTypes"":[{""name"":""performed on"",""description"":""Played on a session."",""from"":""person"",""to"":""RecordingSession"",""questions"":[2,3]},{""name"":""VISITED"",""from"":""Person"",""to"":""Galaxy""}],""guidance"":""Tune titles are Works.""}";
                                WizardResult<WizardOntology> result = await rig.Service.OntologyAsync(rig.H.TenantId, request, ct);
                                Expect(result.Success, "succeeds: " + result.Error);
                                WizardOntology o = result.Value!;
                                List<string> nodes = o.NodeTypes.Select(n => n.Name).ToList();
                                Expect(nodes.Contains("Label") && nodes.Contains("Person") && nodes.Contains("RecordingSession") && nodes.Contains("Subject"), "nodes: " + String.Join(", ", nodes));
                                Expect(!nodes.Contains("Dropped") && nodes.Count(n => n == "Person") == 1, "unlocked draft type replaced; duplicate dropped");
                                Expect(o.NodeTypes.First(n => n.Name == "Person").Questions.SequenceEqual(new[] { 1 }), "question 99 dropped");
                                WizardEdgeType performed = o.EdgeTypes.First(e => e.Name == "PERFORMED_ON");
                                Expect(performed.From == "Person" && performed.To == "RecordingSession", "endpoints resolved to declared types");
                                WizardEdgeType visited = o.EdgeTypes.First(e => e.Name == "VISITED");
                                Expect(visited.To == null && result.Warnings.Any(w => w.Contains("Galaxy")), "unknown endpoint cleared with a warning");
                                Expect(o.Guidance == "Tune titles are Works.", "guidance kept");
                                OntologyVersion version = WizardOntologyBuilder.ToVersion(o);
                                Expect(OntologyVersionValidator.Errors(version).Count == 0, "valid as an ontology version: " + String.Join(" ", OntologyVersionValidator.Errors(version)));
                                Expect(rig.Model.LastChatBody.Contains("Built-in types") && rig.Model.LastChatBody.Contains("Label"), "built-in and locked types shown to the model");

                                request.Draft.Questions = new List<WizardQuestion>();
                                WizardResult<WizardOntology> noQuestions = await rig.Service.OntologyAsync(rig.H.TenantId, request, ct);
                                Expect(noQuestions.StatusCode == 400, "questions are required first");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Prompts_And_Sources", "Locked prompts survive regeneration, long prompts are clipped, and source kinds outside the list become Links",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                rig.Settings.MaxPromptCharacters = 500;
                                WizardGenerateRequest request = rig.Request();
                                request.Draft.Prompts = new WizardPrompts { SystemPrompt = "Mine, keep it.", Locked = new List<string> { "systemPrompt" } };
                                rig.Model.ChatText = "{\"systemPrompt\":\"model version\",\"classifyPrompt\":\"" + new string('x', 900) + "\",\"rewritePrompt\":\"Bird means Charlie Parker.\",\"rerankingPrompt\":\"Prefer primary sources.\"}";
                                WizardResult<WizardPrompts> prompts = await rig.Service.PromptsAsync(rig.H.TenantId, request, ct);
                                Expect(prompts.Success, "succeeds: " + prompts.Error);
                                Expect(prompts.Value!.SystemPrompt == "Mine, keep it." && prompts.Value.Locked.Contains("systemPrompt"), "locked prompt kept");
                                Expect(prompts.Value.ClassifyPrompt!.Length == 500, "clipped to the limit");
                                Expect(prompts.Value.RewritePrompt == "Bird means Charlie Parker.", "others drafted");

                                rig.Model.ChatText = @"{""suggestions"":[{""kind"":""sitemap"",""title"":""Official site"",""detail"":""Crawl it.""},{""kind"":""Magic"",""title"":""Somewhere""},{""kind"":""Text"",""title"":""""}]}";
                                WizardResult<List<WizardSourceSuggestion>> sources = await rig.Service.SourcesAsync(rig.H.TenantId, rig.Request(), ct);
                                Expect(sources.Success && sources.Value!.Count == 2, "empty titles dropped: " + (sources.Value?.Count ?? -1));
                                Expect(sources.Value![0].Kind == "Sitemap" && sources.Value[1].Kind == "Links", "kinds normalized");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Validation", "An empty description, an overlong guidance, and an unknown model endpoint are 400s with no model call",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                Expect((await rig.Service.BriefAsync(rig.H.TenantId, rig.Request("  "), ct)).StatusCode == 400, "description required");
                                WizardGenerateRequest longGuidance = rig.Request();
                                longGuidance.Guidance = new string('g', SubjectWizardService.MaxGuidanceCharacters + 1);
                                Expect((await rig.Service.QuestionsAsync(rig.H.TenantId, longGuidance, ct)).StatusCode == 400, "guidance capped");
                                WizardGenerateRequest unknown = rig.Request();
                                unknown.ModelRunnerId = "mr_missing";
                                WizardResult<WizardBrief> noRunner = await rig.Service.BriefAsync(rig.H.TenantId, unknown, ct);
                                Expect(noRunner.StatusCode == 400 && noRunner.Error!.Contains("completion model"), "unknown endpoint: " + noRunner.Error);
                                Expect(rig.Model.ChatRequestCount == 0, "no model calls");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Commit_Modes", "Commit creates the subject with its prompts and questions; Approve pins a new ontology, Draft leaves it for an approver, and Prompt creates none",
                        executeAsync: async ct =>
                        {
                            await using (WizardRig rig = await WizardRig.CreateAsync(ct))
                            {
                                WizardCommitResult approved = await CommitAsync(rig, "Charlie Parker", WizardOntologyModeEnum.Approve, ct);
                                Subject subject = approved.Subject!;
                                Expect(subject.DisplayName == "Charlie Parker" && subject.Type == "Musician" && subject.Tagline == "Ask about Bird's music", "brief applied");
                                Expect(subject.SystemPrompt == "Answer precisely." && subject.OntologyClassifyPrompt == "Tune titles are Works." && subject.PromptRewritePrompt == "Bird is Charlie Parker.", "prompt additions applied");
                                Expect(subject.Collection == rig.H.CollectionId && subject.InferenceModel == rig.Runner.Id, "defaults resolved");
                                Expect(subject.OntologyDefinitionPrompt!.Contains("Recording"), "the rendered ontology is the fallback prompt");
                                Expect(approved.OntologyMode == WizardOntologyModeEnum.Approve && subject.OntologyVersionId == approved.OntologyVersionId && approved.OntologyVersionId != null, "approved and pinned");
                                OntologyVersion? version = await rig.H.Db.OntologyVersions.ReadAsync(rig.H.TenantId, approved.OntologyVersionId!, ct);
                                Expect(version != null && version.Status == OntologyVersionStatusEnum.Approved && version.NodeTypes.Any(n => n.Name == "Recording"), "an approved version with the drafted types");
                                List<SubjectQuestion> questions = await rig.H.Db.SubjectQuestions.EnumerateBySubjectAsync(rig.H.TenantId, subject.Id, ct);
                                Expect(questions.Count == 2 && questions[0].Question == "Who played on the Savoy sessions?" && questions[1].Kind == SubjectQuestionKindEnum.Timeline, "questions stored in order");

                                WizardCommitResult draft = await CommitAsync(rig, "Charlie Parker", WizardOntologyModeEnum.Draft, ct);
                                Expect(draft.Subject!.UrlSlug != subject.UrlSlug, "a second subject gets its own slug");
                                Expect(draft.Subject.OntologyVersionId == null && draft.OntologyVersionId != null, "draft created, not pinned");
                                OntologyVersion? draftVersion = await rig.H.Db.OntologyVersions.ReadAsync(rig.H.TenantId, draft.OntologyVersionId!, ct);
                                Expect(draftVersion!.Status == OntologyVersionStatusEnum.Draft, "left as a draft");
                                Expect((await rig.H.Db.Ontologies.ReadAsync(rig.H.TenantId, draft.OntologyId!, ct))!.Name != (await rig.H.Db.Ontologies.ReadAsync(rig.H.TenantId, approved.OntologyId!, ct))!.Name, "ontology names are unique");

                                WizardCommitResult prompt = await CommitAsync(rig, "Dizzy Gillespie", WizardOntologyModeEnum.Prompt, ct);
                                Expect(prompt.OntologyId == null && prompt.Subject!.OntologyDefinitionPrompt!.Contains("Recording"), "prompt mode creates no ontology");

                                WizardCommitRequest nameless = new WizardCommitRequest { Draft = new SubjectWizardDraft { Description = "x", Brief = new WizardBrief() } };
                                Expect((await rig.Commit.CommitAsync(rig.H.TenantId, null, nameless, ct)).StatusCode == 400, "a display name is required");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Questions_Storage", "Starter questions replace in one transaction, keep order, stay within their tenant, and go with the subject",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                List<SubjectQuestion> first = await h.Db.SubjectQuestions.ReplaceAsync(h.TenantId, h.SubjectId, new List<SubjectQuestion>
                                {
                                    new SubjectQuestion { TenantId = h.TenantId, SubjectId = h.SubjectId, Question = "A", Kind = SubjectQuestionKindEnum.Overview, Origin = SubjectQuestionOriginEnum.Model },
                                    new SubjectQuestion { TenantId = h.TenantId, SubjectId = h.SubjectId, Question = "B" }
                                }, ct);
                                List<SubjectQuestion> read = await h.Db.SubjectQuestions.EnumerateBySubjectAsync(h.TenantId, h.SubjectId, ct);
                                Expect(read.Select(q => q.Question).SequenceEqual(new[] { "A", "B" }) && read[0].Kind == SubjectQuestionKindEnum.Overview && read[0].Origin == SubjectQuestionOriginEnum.Model && read[1].Position == 1, "stored in order with kind and origin");
                                await h.Db.SubjectQuestions.ReplaceAsync(h.TenantId, h.SubjectId, new List<SubjectQuestion> { new SubjectQuestion { TenantId = h.TenantId, SubjectId = h.SubjectId, Question = "C" } }, ct);
                                Expect((await h.Db.SubjectQuestions.EnumerateBySubjectAsync(h.TenantId, h.SubjectId, ct)).Select(q => q.Question).SequenceEqual(new[] { "C" }), "replaced");
                                Expect((await h.Db.SubjectQuestions.EnumerateBySubjectAsync("ten_other", h.SubjectId, ct)).Count == 0, "tenant scoped");
                                await h.Db.SubjectQuestions.DeleteBySubjectAsync(h.TenantId, h.SubjectId, ct);
                                Expect((await h.Db.SubjectQuestions.EnumerateBySubjectAsync(h.TenantId, h.SubjectId, ct)).Count == 0, "deleted with the subject");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Api_EndToEnd", "Through the API: options, a drafting route, commit with audited approval, starter question routes, and bulk evaluation facts",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer model = new StubModelServer())
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                ModelRunner runner = await server.Database.ModelRunners.CreateAsync(new ModelRunner
                                {
                                    Name = "stub-wizard",
                                    Provider = ModelRunnerProviderEnum.Ollama,
                                    BaseUrl = model.BaseUrl,
                                    ApiType = "Ollama",
                                    Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                                    DefaultModel = "stub",
                                    MaxRetries = 0,
                                    Active = true,
                                    HealthCheckEnabled = false
                                }, ct);
                                string url = server.BaseUrl + "/v1.0/subject-wizard/";

                                ApiResult options = await ApiClientHelper.CallAsync(HttpMethod.Get, url + "options", token, null, ct);
                                Expect(options.StatusCode == 200 && options.Body.Contains("\"defaultOntologyMode\":\"Approve\"") && options.Body.Contains("\"hasCompletionModel\":true"), "options: " + options.Body);

                                model.ChatText = BriefJson;
                                string draft = "{\"description\":\"Charlie Parker, the saxophonist\"}";
                                ApiResult brief = await ApiClientHelper.CallAsync(HttpMethod.Post, url + "brief", token, "{\"modelRunnerId\":\"" + runner.Id + "\",\"draft\":" + draft + "}", ct);
                                Expect(brief.StatusCode == 200 && brief.Body.Contains("\"displayName\":\"Charlie Parker\"") && brief.Body.Contains("\"modelRunnerId\":\"" + runner.Id + "\""), "brief: " + brief.Body);
                                ApiResult empty = await ApiClientHelper.CallAsync(HttpMethod.Post, url + "brief", token, "{\"draft\":{\"description\":\"\"}}", ct);
                                Expect(empty.StatusCode == 400, "empty description is 400");

                                string commitBody = "{\"inferenceModel\":\"" + runner.Id + "\",\"ontologyMode\":\"Approve\",\"draft\":{\"description\":\"Charlie Parker\",\"brief\":{\"displayName\":\"Charlie Parker API\",\"type\":\"Musician\"}," +
                                    "\"questions\":[{\"question\":\"Who played with him?\",\"kind\":\"Relationship\"}]," +
                                    "\"ontology\":{\"nodeTypes\":[{\"name\":\"Person\",\"description\":\"A musician.\",\"questions\":[1]},{\"name\":\"Recording\",\"description\":\"A recording.\"}],\"edgeTypes\":[{\"name\":\"PERFORMED_ON\",\"description\":\"Played on.\",\"from\":\"Person\",\"to\":\"Recording\"}]}}}";
                                ApiResult commit = await ApiClientHelper.CallAsync(HttpMethod.Post, url + "commit", token, commitBody, ct);
                                Expect(commit.StatusCode == 201 && commit.Body.Contains("\"ontologyMode\":\"Approve\""), "commit: " + commit.Body);
                                string subjectId;
                                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(commit.Body))
                                {
                                    subjectId = doc.RootElement.GetProperty("subject").GetProperty("id").GetString() ?? String.Empty;
                                }
                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Expect(audit.Count(a => a.EventType == AuditEventTypeEnum.OntologyGovernance && a.DenialReason != null && a.DenialReason.Contains("new subject wizard")) == 3, "creation, approval, and pin audited");

                                string questionsUrl = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/questions";
                                ApiResult read = await ApiClientHelper.CallAsync(HttpMethod.Get, questionsUrl, token, null, ct);
                                Expect(read.StatusCode == 200 && read.Body.Contains("Who played with him?"), "questions readable: " + read.Body);
                                ApiResult put = await ApiClientHelper.CallAsync(HttpMethod.Put, questionsUrl, token, "{\"questions\":[{\"question\":\"New one\",\"kind\":\"Overview\"},{\"question\":\"  \"}]}", ct);
                                Expect(put.StatusCode == 200 && put.Body.Contains("New one") && !put.Body.Contains("Who played"), "questions replaced, blanks skipped: " + put.Body);
                                Expect((await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/sub_missing/questions", token, null, ct)).StatusCode == 404, "unknown subject is 404");

                                string facts = "{\"facts\":[{\"subjectId\":\"" + subjectId + "\",\"question\":\"Q1\",\"expectedAnswer\":\"A1\"},{\"subjectId\":\"" + subjectId + "\",\"question\":\"Q2\",\"expectedAnswer\":\"A2\",\"category\":\"wizard\"}]}";
                                ApiResult bulk = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/facts/bulk", token, facts, ct);
                                Expect(bulk.StatusCode == 201 && bulk.Body.Contains("\"created\":2"), "bulk facts: " + bulk.Body);
                                ApiResult missingSubject = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/facts/bulk", token, "{\"facts\":[{\"subjectId\":\"sub_missing\",\"question\":\"Q\",\"expectedAnswer\":\"A\"}]}", ct);
                                Expect(missingSubject.StatusCode == 404, "unknown subject is 404");
                                Expect((await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/facts/bulk", token, "{\"facts\":[]}", ct)).StatusCode == 400, "empty list is 400");
                            }
                        }),

                    new TestCaseDescriptor("SubjectWizard", "Mcp_DraftAndCreate", "The MCP tools draft a subject in one call (four model steps) and create it from the returned draft",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer model = new StubModelServer())
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                ModelRunner runner = await server.Database.ModelRunners.CreateAsync(new ModelRunner
                                {
                                    Name = "stub-mcp-wizard",
                                    Provider = ModelRunnerProviderEnum.Ollama,
                                    BaseUrl = model.BaseUrl,
                                    ApiType = "Ollama",
                                    Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                                    DefaultModel = "stub",
                                    MaxRetries = 0,
                                    Active = true,
                                    HealthCheckEnabled = false
                                }, ct);
                                model.ChatQueue.Enqueue(BriefJson);
                                model.ChatQueue.Enqueue(@"{""questions"":[{""question"":""Who played on the Savoy sessions?"",""kind"":""relationship""},{""question"":""Where was he born?"",""kind"":""fact""}]}");
                                model.ChatQueue.Enqueue(@"{""nodeTypes"":[{""name"":""Person"",""description"":""A musician."",""questions"":[1,2]},{""name"":""Recording"",""description"":""A recording."",""questions"":[1]}],""edgeTypes"":[{""name"":""PERFORMED_ON"",""from"":""Person"",""to"":""Recording"",""questions"":[1]}],""guidance"":""Albums are recordings.""}");
                                model.ChatQueue.Enqueue(@"{""systemPrompt"":""Be precise."",""classifyPrompt"":""Albums are Recordings."",""rewritePrompt"":""Bird is Charlie Parker."",""rerankingPrompt"":""Prefer primary sources.""}");

                                string draftCall = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_draft_subject\",\"arguments\":{\"description\":\"Charlie Parker\",\"modelRunnerId\":\"" + runner.Id + "\",\"questionCount\":2}}}";
                                ApiResult drafted = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, draftCall, ct);
                                Expect(drafted.StatusCode == 200 && !drafted.Body.Contains("\"error\"") && model.ChatRequestCount == 4, "drafted in four calls: " + drafted.Body);
                                string draftJson = ExtractToolJson(drafted.Body);
                                string innerDraft;
                                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(draftJson))
                                {
                                    innerDraft = doc.RootElement.GetProperty("draft").GetRawText();
                                    Expect(doc.RootElement.GetProperty("draft").GetProperty("brief").GetProperty("displayName").GetString() == "Charlie Parker", "brief in the draft");
                                    Expect(doc.RootElement.GetProperty("renderedOntology").GetString()!.Contains("PERFORMED_ON"), "rendered ontology returned");
                                }

                                string createCall = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject_from_draft\",\"arguments\":{\"ontologyMode\":\"Draft\",\"inferenceModel\":\"" + runner.Id + "\",\"draft\":" + innerDraft + "}}}";
                                ApiResult created = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, createCall, ct);
                                Expect(created.StatusCode == 200 && !created.Body.Contains("\"error\""), "created: " + created.Body);
                                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(ExtractToolJson(created.Body)))
                                {
                                    Expect(doc.RootElement.GetProperty("ontologyMode").GetString() == "Draft" && doc.RootElement.GetProperty("questions").GetArrayLength() == 2, "draft ontology and two questions");
                                    Expect(doc.RootElement.GetProperty("subject").GetProperty("systemPrompt").GetString() == "Be precise.", "prompts applied");
                                }
                                string badCall = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject_from_draft\",\"arguments\":{}}}";
                                Expect((await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, badCall, ct)).Body.Contains("\"error\""), "a missing draft is an error");
                            }
                        })
                });
        }

        private static async Task<WizardCommitResult> CommitAsync(WizardRig rig, string name, WizardOntologyModeEnum mode, CancellationToken ct)
        {
            WizardCommitRequest request = new WizardCommitRequest
            {
                OntologyMode = mode,
                InferenceModel = rig.Runner.Id,
                Draft = new SubjectWizardDraft
                {
                    Description = "A jazz musician",
                    Brief = new WizardBrief { DisplayName = name, Type = "Musician", Tagline = "Ask about Bird's music" },
                    Questions = new List<WizardQuestion>
                    {
                        new WizardQuestion { Question = "Who played on the Savoy sessions?", Kind = SubjectQuestionKindEnum.Relationship },
                        new WizardQuestion { Question = "How did his style change?", Kind = SubjectQuestionKindEnum.Timeline }
                    },
                    Ontology = new WizardOntology
                    {
                        NodeTypes = new List<WizardNodeType>
                        {
                            new WizardNodeType { Name = "Person", Description = "A musician.", Questions = new List<int> { 1 } },
                            new WizardNodeType { Name = "Recording", Description = "A recorded performance.", Questions = new List<int> { 1, 2 } }
                        },
                        EdgeTypes = new List<WizardEdgeType>
                        {
                            new WizardEdgeType { Name = "PERFORMED_ON", Description = "A musician played on a recording.", From = "Person", To = "Recording", Questions = new List<int> { 1 } }
                        }
                    },
                    Prompts = new WizardPrompts { SystemPrompt = "Answer precisely.", ClassifyPrompt = "Tune titles are Works.", RewritePrompt = "Bird is Charlie Parker." }
                }
            };
            WizardResult<WizardCommitResult> result = await rig.Commit.CommitAsync(rig.H.TenantId, null, request, ct);
            if (!result.Success) throw new Exception("commit failed: " + result.StatusCode + " " + result.Error);
            return result.Value!;
        }

        // MCP tool results carry the tool's JSON as text in result.content[0].text.
        private static string ExtractToolJson(string body)
        {
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body))
            {
                return doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
            }
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
