namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Ontology governance through the real ingestion pipeline against a stub model: a pinned version shapes the prompt
    /// and its rules shape the graph, the classification cache answers identical requests, taxonomy concepts are hinted
    /// and linked, quarantined elements can be released, and the retag, validate, and drift-check operations and the
    /// authoring assistant work end to end.
    /// </summary>
    public static class OntologyPipelineSuite
    {
        private const string _EmptyReply = "{\"nodes\":[],\"edges\":[]}";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "OntologyPipeline",
                displayName: "Ontology governance in the ingestion pipeline",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("OntologyPipeline", "PinnedVersion_ShapesPromptAndGraph", "A pinned version's definition reaches the model and its rules warn, reverse, drop, and quarantine",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = OntologyTestData.GovernedReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                OntologyVersion version = await PinAsync(h, OntologyTestData.Governed(), ct);
                                string jobId = await h.IngestAsync(new FakeContentFetcher("Ada and Grace work at Acme. The Book was written by Ada."), new FakeSemanticProcessor(), ct);
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                Check(job.Status == IngestionStatusEnum.Completed, "expected Completed, got " + job.Status + " " + job.Error);
                                Check(stub.LastChatBody.Contains("Prefer Organization for named teams"), "the version's guidance reaches the classifier");
                                Check(await FindAsync(h, "Planet", "Mars", ct) == null, "the undeclared node is held out of the graph");
                                GraphNode grace = await FindAsync(h, "Person", "Grace", ct) ?? throw new Exception("Grace missing");
                                GraphNode acme = await FindAsync(h, "Organization", "Acme", ct) ?? throw new Exception("Acme missing");
                                List<GraphEdge> graceEdges = await h.Graph.GetEdgesAsync(grace.Id, ct);
                                Check(graceEdges.Any(e => e.EdgeType == "WORKS_FOR" && e.FromNodeId == grace.Id && e.ToNodeId == acme.Id), "the backwards WORKS_FOR was stored reversed");

                                List<OntologyViolation> violations = await h.Db.OntologyViolations.EnumerateAsync(h.TenantId, h.SubjectId, null, jobId, null, ct);
                                Check(violations.Count == 6 && job.Completeness.OntologyViolations == 6, "six violations recorded, got " + violations.Count);
                                Check(violations.All(v => v.OntologyVersionId == version.Id && v.LinkId == h.LinkId), "violations name the version and link");
                                Check(violations.Count(v => v.Status == OntologyViolationStatusEnum.Quarantined) == 2, "two quarantined");
                                Check(job.Warnings.Any(w => w.Contains("quarantined")), "the job warns about the held-out elements");
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "Quarantine_ReleaseAndDismiss", "A released node and relationship enter the graph; a dismissed one does not; a reviewed element cannot be reviewed again",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = OntologyTestData.GovernedReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                await PinAsync(h, OntologyTestData.Governed(), ct);
                                await h.IngestAsync(new FakeContentFetcher("Ada, Grace, Acme, and The Book."), new FakeSemanticProcessor(), ct);
                                List<OntologyViolation> held = await h.Db.OntologyViolations.EnumerateAsync(h.TenantId, h.SubjectId, OntologyViolationStatusEnum.Quarantined, null, null, ct);
                                OntologyViolationReviewer reviewer = new OntologyViolationReviewer(h.Db, new FakeGraphRepositoryFactory(h.Graph));

                                OntologyViolation node = held.Single(v => v.ElementKind == OntologyElementKindEnum.Node);
                                OntologyResult<OntologyViolation> released = await reviewer.ReleaseAsync(node, "usr_reviewer", ct);
                                Check(released.Succeeded && released.Value!.Status == OntologyViolationStatusEnum.Released, "the node is released");
                                Check(await FindAsync(h, "Planet", "Mars", ct) != null, "the released node is in the graph");
                                Check((await reviewer.ReleaseAsync(node, "usr_reviewer", ct)).StatusCode == 409, "a released element cannot be released again");

                                OntologyViolation edge = held.Single(v => v.ElementKind == OntologyElementKindEnum.Edge);
                                GraphNode book = await FindAsync(h, "Work", "The Book", ct) ?? throw new Exception("book missing");
                                int before = (await h.Graph.GetEdgesAsync(book.Id, ct)).Count(e => e.EdgeType == "CREATED_BY");
                                OntologyResult<OntologyViolation> dismissed = await reviewer.DismissAsync(edge, "usr_reviewer", ct);
                                Check(dismissed.Succeeded && (await h.Graph.GetEdgesAsync(book.Id, ct)).Count(e => e.EdgeType == "CREATED_BY") == before, "a dismissed relationship stays out");
                                edge.Status = OntologyViolationStatusEnum.Quarantined;
                                OntologyResult<OntologyViolation> edgeReleased = await reviewer.ReleaseAsync(edge, "usr_reviewer", ct);
                                Check(edgeReleased.Succeeded && (await h.Graph.GetEdgesAsync(book.Id, ct)).Count(e => e.EdgeType == "CREATED_BY") == before + 1, "a released relationship links its endpoints");

                                OntologyViolation missingEndpoint = new OntologyViolation { TenantId = h.TenantId, SubjectId = h.SubjectId, ElementKind = OntologyElementKindEnum.Edge, Status = OntologyViolationStatusEnum.Quarantined, EdgeType = "WORKS_FOR", FromNodeType = "Person", FromNodeName = "Nobody", ToNodeType = "Organization", ToNodeName = "Acme" };
                                Check((await reviewer.ReleaseAsync(missingEndpoint, null, ct)).StatusCode == 409, "a relationship whose endpoint is missing cannot be released");
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "Cache_AnswersIdenticalRequests", "Identical content on a second link is classified from the cache; with the cache off, the model is called again",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = "{\"nodes\":[{\"ref\":\"n1\",\"nodeType\":\"Organization\",\"name\":\"Acme\"}],\"edges\":[]}" })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                FakeContentFetcher fetcher = new FakeContentFetcher("Acme makes anvils.");
                                await h.IngestAsync(fetcher, new FakeSemanticProcessor(), ct);
                                int calls = stub.ChatRequestCount;
                                string second = await h.IngestOtherLinkAsync(fetcher, ct);
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, second, ct) ?? throw new Exception("job gone");
                                Check(stub.ChatRequestCount == calls, "the second classification came from the cache (" + stub.ChatRequestCount + " calls, expected " + calls + ")");
                                Check(job.Completeness.ClassificationCacheHits == 1, "the job counts the cache hit");

                                Subject subject = await h.Db.Subjects.ReadAsync(h.TenantId, h.SubjectId, ct) ?? throw new Exception("subject gone");
                                subject.ClassificationCacheEnabled = false;
                                await h.Db.Subjects.UpdateAsync(subject, ct);
                                await h.IngestOtherLinkAsync(fetcher, ct);
                                Check(stub.ChatRequestCount == calls + 1, "with the cache off the model is called");

                                ClassificationCache cache = new ClassificationCache(h.Db, h.Blobs);
                                Check(await cache.CountAsync(h.TenantId, h.SubjectId, ct) == 1, "one cached entry for the subject");
                                Check(await cache.ClearAsync(h.TenantId, h.SubjectId, ct) == 1 && await cache.CountAsync(h.TenantId, null, ct) == 0, "clearing removes it");
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "Taxonomy_HintsAndLinks", "Matched concepts are hinted to the classifier and linked to the cell, with the broader concept linked too",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = _EmptyReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                await PinAsync(h, OntologyTestData.Governed(), ct);
                                string jobId = await h.IngestAsync(new FakeContentFetcher("Our platform runs on k8s."), new FakeSemanticProcessor(), ct);
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                Check(job.Completeness.TaxonomyMatches == 1, "one concept matched, got " + job.Completeness.TaxonomyMatches);
                                Check(stub.LastChatBody.Contains("Kubernetes (Topic)"), "the classifier sees the hint");
                                GraphNode kube = await FindAsync(h, "Topic", "Kubernetes", ct) ?? throw new Exception("concept node missing");
                                GraphNode cloud = await FindAsync(h, "Topic", "Cloud computing", ct) ?? throw new Exception("broader concept missing");
                                Check(kube.Tags[Ontology.TagAssertedBy] == Ontology.AssertedByTaxonomy && kube.Tags[Ontology.TagTaxonomyConcept] == "k8s", "the concept node is tagged");
                                List<GraphEdge> edges = await h.Graph.GetEdgesAsync(kube.Id, ct);
                                Check(edges.Any(e => e.EdgeType == "ABOUT" && e.ToNodeId == kube.Id && e.Tags[Ontology.TagAssertedByJob] == jobId), "the cell is linked ABOUT the concept");
                                Check(edges.Any(e => e.EdgeType == Ontology.EdgeBroader && e.ToNodeId == cloud.Id), "the concept is linked to its broader concept");
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "Operations_RetagValidateDrift", "Retag follows a new taxonomy, validation records findings once, and a drift check of a stable model reports no drift",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = OntologyTestData.GovernedReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                OntologyVersion first = await PinAsync(h, OntologyTestData.Governed(), ct);
                                await h.IngestAsync(new FakeContentFetcher("Ada runs k8s on the platform."), new FakeSemanticProcessor(), ct);
                                OntologyOperationProcessor processor = new OntologyOperationProcessor(h.Db, new FakeGraphRepositoryFactory(h.Graph), new Aes256Cipher("test-signing-key"), new PolyPromptClassifier(Quiet()), new OntologySettings());

                                OntologyVersion next = OntologyTestData.Governed();
                                next.Concepts.RemoveAll(c => c.Key == "k8s");
                                next.Concepts.Add(new OntologyConcept { Key = "platform", PrefLabel = "Platform", NodeType = "Topic" });
                                OntologyVersion second = await StoreApprovedAsync(h, first.OntologyId, 2, next, ct);
                                Subject subject = await h.Db.Subjects.ReadAsync(h.TenantId, h.SubjectId, ct) ?? throw new Exception("subject gone");
                                OntologyResult<SubjectPinResult> pin = await new OntologyService(h.Db).PinAsync(subject, second.Id, ct);
                                Check(pin.Succeeded && pin.Value!.TaxonomyChanged, "the pin reports the taxonomy change");

                                OntologyOperation retag = await processor.ProcessAsync(await QueueAsync(h, OntologyOperationKindEnum.Retag, ct), ct);
                                Check(retag.Status == OntologyOperationStatusEnum.Succeeded && retag.Added == 1 && retag.Removed == 1 && retag.Changed == 1, "retag added 1 and removed 1: " + retag.Added + "/" + retag.Removed + " " + retag.Error);
                                GraphNode kube = await FindAsync(h, "Topic", "Kubernetes", ct) ?? throw new Exception("concept node missing");
                                Check(!(await h.Graph.GetEdgesAsync(kube.Id, ct)).Any(e => e.EdgeType == "ABOUT"), "the stale link is gone");
                                Check((await h.Db.OntologyOperations.EnumerateItemsAsync(h.TenantId, retag.Id, ct)).Count == 1, "the retagged cell is listed");

                                OntologyOperation validate = await processor.ProcessAsync(await QueueAsync(h, OntologyOperationKindEnum.Validate, ct), ct);
                                Check(validate.Status == OntologyOperationStatusEnum.Succeeded && validate.Changed > 0, "validation found the person without content: " + validate.Error);
                                await processor.ProcessAsync(await QueueAsync(h, OntologyOperationKindEnum.Validate, ct), ct);
                                List<OntologyViolation> findings = (await h.Db.OntologyViolations.EnumerateAsync(h.TenantId, h.SubjectId, null, null, null, ct)).Where(v => v.OperationId != null).ToList();
                                Check(findings.Count == validate.Changed, "a second validation replaces the first one's findings");

                                OntologyOperation drift = await processor.ProcessAsync(await QueueAsync(h, OntologyOperationKindEnum.DriftCheck, ct), ct);
                                Check(drift.Status == OntologyOperationStatusEnum.Succeeded && drift.Total == 1 && drift.Changed == 0 && drift.DriftRate == 0, "a stable model shows no drift: " + drift.Error);
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "Proposer_DraftsFromContent", "The authoring assistant turns a proposal into a draft, extends a version without losing its types, and refuses unusable replies",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = _EmptyReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                await h.IngestAsync(new FakeContentFetcher("Ada writes compilers at Acme."), new FakeSemanticProcessor(), ct);
                                OntologyService service = new OntologyService(h.Db);
                                OntologyResult<TenantOntology> created = await service.CreateAsync(h.TenantId, new OntologyCreateRequest { Name = "Engineering", Template = "Default" }, null, ct);
                                TenantOntology ontology = created.Value ?? throw new Exception("create failed: " + created.Error);
                                string baseId = (await h.Db.OntologyVersions.EnumerateAsync(h.TenantId, ontology.Id, ct)).Single().Id;
                                string runnerId = (await h.Db.ModelRunners.EnumerateAsync(h.TenantId, ct)).First(r => r.Name.StartsWith("stub-chat-", StringComparison.Ordinal)).Id;
                                OntologyProposer proposer = new OntologyProposer(h.Db, new FakeGraphRepositoryFactory(h.Graph), new Aes256Cipher("test-signing-key"), new OntologySettings(), Quiet());

                                stub.ChatText = "Here you go:\n{\"nodeTypes\":[{\"name\":\"Compiler\",\"description\":\"A program that translates code.\"},{\"name\":\"person\",\"description\":\"dup\"}]," +
                                    "\"edgeTypes\":[{\"name\":\"writes\",\"description\":\"Authorship of software.\"}],\"edgeEndpoints\":[{\"edgeType\":\"WRITES\",\"fromNodeType\":\"Person\",\"toNodeType\":\"Compiler\"},{\"edgeType\":\"WRITES\",\"fromNodeType\":\"Ghost\",\"toNodeType\":\"Compiler\"}]," +
                                    "\"guidance\":\"Model software as Compiler.\",\"changeSummary\":\"Adds software.\"}";
                                OntologyResult<OntologyVersion> extended = await proposer.ProposeAsync(ontology, new OntologyProposeRequest { SubjectId = h.SubjectId, ModelRunnerId = runnerId, BasedOnVersionId = baseId, Language = "English" }, "usr_author", ct);
                                OntologyVersion draft = extended.Value ?? throw new Exception("propose failed: " + extended.Error);
                                Check(extended.StatusCode == 201 && draft.Status == OntologyVersionStatusEnum.Draft && draft.VersionNumber == 2, "a new draft version 2");
                                Check(draft.NodeTypes.Count(t => OntologyTypeResolver.Same(t.Name, "Person")) == 1 && draft.NodeTypes.Any(t => t.Name == "Compiler"), "existing types kept, new type added, duplicate ignored");
                                Check(draft.EdgeTypes.Any(t => t.Name == "WRITES") && draft.Rules.Count(r => r.EdgeType == "WRITES") == 1, "the edge type and its valid endpoint are added; the ghost endpoint is not");
                                Check(draft.ChangeSummary!.Contains("Adds software.") && draft.Problems.Count == 0, "the draft is ready to review");
                                Check(stub.LastChatBody.Contains("Ada writes compilers"), "the subject's cells were sampled");

                                stub.ChatText = "I cannot help with that.";
                                Check((await proposer.ProposeAsync(ontology, new OntologyProposeRequest { SampleText = "Some text.", ModelRunnerId = runnerId }, null, ct)).StatusCode == 502, "an unusable reply is a 502");
                                Check((await proposer.ProposeAsync(ontology, new OntologyProposeRequest { ModelRunnerId = runnerId }, null, ct)).StatusCode == 400, "no sample is a 400");
                            }
                        }),

                    new TestCaseDescriptor("OntologyPipeline", "NoPin_KeepsPromptBehavior", "Without a pinned version the output contract prompt is used and nothing is validated",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer { ChatText = OntologyTestData.GovernedReply })
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                            {
                                string jobId = await h.IngestAsync(new FakeContentFetcher("Ada and Mars."), new FakeSemanticProcessor(), ct);
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                Check(job.Status == IngestionStatusEnum.Completed && job.Completeness.OntologyViolations == 0, "no validation without a pinned version");
                                Check(stub.LastChatBody.Contains("Respond with ONLY a JSON object"), "the seeded output contract prompt is used");
                                Check(await FindAsync(h, "Planet", "Mars", ct) != null, "custom types are kept");
                            }
                        })
                });
        }

        private static async Task<OntologyVersion> PinAsync(IngestionHarness h, OntologyVersion contents, CancellationToken ct)
        {
            TenantOntology ontology = await h.Db.Ontologies.CreateAsync(new TenantOntology { TenantId = h.TenantId, Name = "Governed " + Guid.NewGuid().ToString("N").Substring(0, 6) }, ct);
            OntologyVersion version = await StoreApprovedAsync(h, ontology.Id, 1, contents, ct);
            Subject subject = await h.Db.Subjects.ReadAsync(h.TenantId, h.SubjectId, ct) ?? throw new Exception("subject gone");
            subject.OntologyVersionId = version.Id;
            // Background operations resolve the subject's inference model (there is no job to name a runner).
            subject.InferenceModel = (await h.Db.ModelRunners.EnumerateAsync(h.TenantId, ct)).First(r => r.Name.StartsWith("stub-chat-", StringComparison.Ordinal)).Id;
            await h.Db.Subjects.UpdateAsync(subject, ct);
            return version;
        }

        private static async Task<OntologyVersion> StoreApprovedAsync(IngestionHarness h, string ontologyId, int number, OntologyVersion contents, CancellationToken ct)
        {
            contents.TenantId = h.TenantId;
            contents.OntologyId = ontologyId;
            contents.VersionNumber = number;
            contents.Status = OntologyVersionStatusEnum.Approved;
            contents.ApprovedUtc = DateTime.UtcNow;
            await h.Db.OntologyVersions.CreateAsync(contents, ct);
            return await h.Db.OntologyVersions.ReadAsync(h.TenantId, contents.Id, ct) ?? throw new Exception("version gone");
        }

        private static async Task<OntologyOperation> QueueAsync(IngestionHarness h, OntologyOperationKindEnum kind, CancellationToken ct)
        {
            OntologyOperation queued = await h.Db.OntologyOperations.CreateAsync(new OntologyOperation { TenantId = h.TenantId, SubjectId = h.SubjectId, Kind = kind, SampleSize = 5 }, ct);
            OntologyOperation? claimed = await h.Db.OntologyOperations.ClaimNextQueuedAsync("test-worker", ct);
            if (claimed == null || claimed.Id != queued.Id) throw new Exception("the operation was not claimed");
            return claimed;
        }

        private static Task<GraphNode?> FindAsync(IngestionHarness h, string type, string name, CancellationToken ct)
        {
            return h.Graph.FindNodeByCanonicalAsync(type, name, h.SubjectId, ct);
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
