namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Ontology lifecycle rules: creating ontologies and drafts, saving drafts, approving and retiring versions, deleting,
    /// comparing, and pinning subjects. A draft is the only editable state; an approved version is immutable; a version
    /// a subject pins cannot be retired or deleted.
    /// </summary>
    public class OntologyService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public OntologyService(DatabaseDriverBase db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>Create an ontology and its first draft (empty, from a template, or a copy of a version).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="userId">Creating user, if known.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology (201), or 400/404/409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<OntologyResult<TenantOntology>> CreateAsync(string tenantId, OntologyCreateRequest request, string? userId, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? nameProblem = CheckName(request.Name);
            if (nameProblem != null) return OntologyResult<TenantOntology>.Fail(400, nameProblem);
            string name = request.Name!.Trim();
            if (await _Db.Ontologies.ReadByNameAsync(tenantId, name, token).ConfigureAwait(false) != null)
                return OntologyResult<TenantOntology>.Fail(409, "An ontology named '" + name + "' already exists.");

            OntologyVersion? content = null;
            if (!String.IsNullOrWhiteSpace(request.Template))
            {
                content = OntologyTemplates.Build(request.Template);
                if (content == null) return OntologyResult<TenantOntology>.Fail(400, "Unknown template '" + request.Template + "'.");
            }
            else if (!String.IsNullOrWhiteSpace(request.CopyFromVersionId))
            {
                content = await _Db.OntologyVersions.ReadAsync(tenantId, request.CopyFromVersionId!, token).ConfigureAwait(false);
                if (content == null) return OntologyResult<TenantOntology>.Fail(404, "Version to copy not found.");
            }

            TenantOntology ontology = new TenantOntology { TenantId = tenantId, Name = name, Description = request.Description };
            await _Db.Ontologies.CreateAsync(ontology, token).ConfigureAwait(false);
            OntologyVersion draft = NewDraft(ontology, 1, content, userId);
            if (content == null) draft.ChangeSummary = "First version.";
            else if (!String.IsNullOrWhiteSpace(request.Template)) draft.ChangeSummary = "Created from the " + request.Template + " template.";
            else draft.ChangeSummary = "Copied from version " + content.VersionNumber + ".";
            await _Db.OntologyVersions.CreateAsync(draft, token).ConfigureAwait(false);
            return OntologyResult<TenantOntology>.Ok(ontology, 201);
        }

        /// <summary>Rename or re-describe an ontology.</summary>
        /// <param name="ontology">The existing ontology.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology, or 400/409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<OntologyResult<TenantOntology>> UpdateAsync(TenantOntology ontology, OntologyUpdateRequest request, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Name != null)
            {
                string? nameProblem = CheckName(request.Name);
                if (nameProblem != null) return OntologyResult<TenantOntology>.Fail(400, nameProblem);
                string name = request.Name.Trim();
                TenantOntology? clash = await _Db.Ontologies.ReadByNameAsync(ontology.TenantId, name, token).ConfigureAwait(false);
                if (clash != null && clash.Id != ontology.Id) return OntologyResult<TenantOntology>.Fail(409, "An ontology named '" + name + "' already exists.");
                ontology.Name = name;
            }
            ontology.Description = request.Description;
            await _Db.Ontologies.UpdateAsync(ontology, token).ConfigureAwait(false);
            return OntologyResult<TenantOntology>.Ok(ontology);
        }

        /// <summary>Delete an ontology and all of its versions, unless a subject pins one of them.</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True (200), or 409 when pinned.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ontology"/> is null.</exception>
        public async Task<OntologyResult<bool>> DeleteAsync(TenantOntology ontology, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(ontology.TenantId, ontology.Id, token).ConfigureAwait(false);
            List<OntologySubjectReference> pins = await PinsAsync(ontology.TenantId, versions.Select(v => v.Id), token).ConfigureAwait(false);
            if (pins.Count > 0) return OntologyResult<bool>.Fail(409, "Subjects pin versions of this ontology (" + String.Join(", ", pins.Select(p => p.DisplayName)) + "). Unpin them first.");
            await _Db.Ontologies.DeleteAsync(ontology.TenantId, ontology.Id, token).ConfigureAwait(false);
            return OntologyResult<bool>.Ok(true);
        }

        /// <summary>Start a new draft that copies a version (by default the newest).</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="basedOnVersionId">Version to copy, or null for the newest.</param>
        /// <param name="userId">Creating user, if known.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft (201), or 404.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ontology"/> is null.</exception>
        public async Task<OntologyResult<OntologyVersion>> CreateDraftAsync(TenantOntology ontology, string? basedOnVersionId, string? userId, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            OntologyVersion? source = null;
            if (!String.IsNullOrWhiteSpace(basedOnVersionId))
            {
                source = await _Db.OntologyVersions.ReadAsync(ontology.TenantId, basedOnVersionId!, token).ConfigureAwait(false);
                if (source == null || source.OntologyId != ontology.Id) return OntologyResult<OntologyVersion>.Fail(404, "Version to copy not found in this ontology.");
            }
            else
            {
                List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(ontology.TenantId, ontology.Id, token).ConfigureAwait(false);
                if (versions.Count > 0) source = await _Db.OntologyVersions.ReadAsync(ontology.TenantId, versions[0].Id, token).ConfigureAwait(false);
            }
            int number = await _Db.OntologyVersions.NextVersionNumberAsync(ontology.TenantId, ontology.Id, token).ConfigureAwait(false);
            OntologyVersion draft = NewDraft(ontology, number, source, userId);
            await _Db.OntologyVersions.CreateAsync(draft, token).ConfigureAwait(false);
            draft.Problems = OntologyVersionValidator.Problems(draft);
            return OntologyResult<OntologyVersion>.Ok(draft, 201);
        }

        /// <summary>Replace a draft's contents. Rule ids already in the draft are kept; any other id is replaced.</summary>
        /// <param name="existing">The stored draft.</param>
        /// <param name="incoming">The new contents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved draft with its approval problems, or 400/409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<OntologyResult<OntologyVersion>> SaveDraftAsync(OntologyVersion existing, OntologyVersion incoming, CancellationToken token = default)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (existing.Status != OntologyVersionStatusEnum.Draft)
                return OntologyResult<OntologyVersion>.Fail(409, "Only a draft can be edited; this version is " + existing.Status + ". Start a new draft from it instead.");

            HashSet<string> ownRuleIds = new HashSet<string>(existing.Rules.Select(r => r.Id), StringComparer.Ordinal);
            foreach (OntologyRule rule in incoming.Rules)
            {
                if (!ownRuleIds.Contains(rule.Id)) rule.Id = String.Empty;
            }
            existing.Guidance = String.IsNullOrWhiteSpace(incoming.Guidance) ? null : incoming.Guidance;
            existing.UndeclaredTypeAction = incoming.UndeclaredTypeAction;
            existing.ChangeSummary = incoming.ChangeSummary;
            existing.NodeTypes = incoming.NodeTypes;
            existing.EdgeTypes = incoming.EdgeTypes;
            existing.Rules = incoming.Rules;
            existing.Concepts = incoming.Concepts;

            List<string> errors = OntologyVersionValidator.Errors(existing);
            if (errors.Count > 0) return OntologyResult<OntologyVersion>.Fail(400, "The ontology version is not valid: " + String.Join(" ", errors), errors);
            await _Db.OntologyVersions.UpdateAsync(existing, token).ConfigureAwait(false);
            existing.Problems = OntologyVersionValidator.Problems(existing);
            return OntologyResult<OntologyVersion>.Ok(existing);
        }

        /// <summary>Approve a draft, making it immutable and available to pin. The contents must have no errors or problems.</summary>
        /// <param name="version">The draft, with contents loaded.</param>
        /// <param name="userId">Approving user, if known.</param>
        /// <param name="changeSummary">Optional change summary.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The approved version, or 400/409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public async Task<OntologyResult<OntologyVersion>> ApproveAsync(OntologyVersion version, string? userId, string? changeSummary, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (version.Status != OntologyVersionStatusEnum.Draft) return OntologyResult<OntologyVersion>.Fail(409, "Only a draft can be approved; this version is " + version.Status + ".");
            List<string> issues = OntologyVersionValidator.Errors(version);
            issues.AddRange(OntologyVersionValidator.Problems(version));
            if (issues.Count > 0) return OntologyResult<OntologyVersion>.Fail(400, "The version cannot be approved until its problems are fixed.", issues);

            version.Status = OntologyVersionStatusEnum.Approved;
            version.ApprovedByUserId = userId;
            version.ApprovedUtc = DateTime.UtcNow;
            if (!String.IsNullOrWhiteSpace(changeSummary)) version.ChangeSummary = changeSummary;
            await _Db.OntologyVersions.UpdateStatusAsync(version, token).ConfigureAwait(false);
            version.Problems = new List<string>();
            return OntologyResult<OntologyVersion>.Ok(version);
        }

        /// <summary>Retire an approved version so it cannot be newly pinned. Refused while a subject pins it.</summary>
        /// <param name="version">The version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The retired version, or 409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public async Task<OntologyResult<OntologyVersion>> RetireAsync(OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (version.Status != OntologyVersionStatusEnum.Approved) return OntologyResult<OntologyVersion>.Fail(409, "Only an approved version can be retired; this version is " + version.Status + ".");
            List<OntologySubjectReference> pins = await PinsAsync(version.TenantId, new List<string> { version.Id }, token).ConfigureAwait(false);
            if (pins.Count > 0) return OntologyResult<OntologyVersion>.Fail(409, "Subjects pin this version (" + String.Join(", ", pins.Select(p => p.DisplayName)) + "). Move them to another version first.");
            version.Status = OntologyVersionStatusEnum.Retired;
            version.RetiredUtc = DateTime.UtcNow;
            await _Db.OntologyVersions.UpdateStatusAsync(version, token).ConfigureAwait(false);
            return OntologyResult<OntologyVersion>.Ok(version);
        }

        /// <summary>Delete a draft version.</summary>
        /// <param name="version">The version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True, or 409 when it is not a draft.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public async Task<OntologyResult<bool>> DeleteVersionAsync(OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (version.Status != OntologyVersionStatusEnum.Draft) return OntologyResult<bool>.Fail(409, "Only a draft can be deleted; retire an approved version instead.");
            await _Db.OntologyVersions.DeleteAsync(version.TenantId, version.Id, token).ConfigureAwait(false);
            return OntologyResult<bool>.Ok(true);
        }

        /// <summary>Compare a version with another (by default the version it was copied from, else the previous number).</summary>
        /// <param name="version">The version, with contents loaded.</param>
        /// <param name="againstVersionId">The version to compare with, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The differences, or 404 when there is nothing to compare with.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public async Task<OntologyResult<OntologyVersionDiff>> DiffAsync(OntologyVersion version, string? againstVersionId, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            string? otherId = againstVersionId ?? version.BasedOnVersionId;
            if (String.IsNullOrWhiteSpace(otherId))
            {
                List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(version.TenantId, version.OntologyId, token).ConfigureAwait(false);
                OntologyVersion? previous = versions.Where(v => v.VersionNumber < version.VersionNumber).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                otherId = previous?.Id;
            }
            if (String.IsNullOrWhiteSpace(otherId)) return OntologyResult<OntologyVersionDiff>.Fail(404, "There is no earlier version to compare with.");
            OntologyVersion? other = await _Db.OntologyVersions.ReadAsync(version.TenantId, otherId!, token).ConfigureAwait(false);
            if (other == null) return OntologyResult<OntologyVersionDiff>.Fail(404, "Version to compare with not found.");
            return OntologyResult<OntologyVersionDiff>.Ok(OntologyDiffer.Compare(other, version));
        }

        /// <summary>Pin a subject to an approved version, or unpin it.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="versionId">The version to pin, or null to unpin.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or 400/404.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subject"/> is null.</exception>
        public async Task<OntologyResult<SubjectPinResult>> PinAsync(Subject subject, string? versionId, CancellationToken token = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            OntologyVersion? next = null;
            if (!String.IsNullOrWhiteSpace(versionId))
            {
                next = await _Db.OntologyVersions.ReadAsync(subject.TenantId, versionId!, token).ConfigureAwait(false);
                if (next == null) return OntologyResult<SubjectPinResult>.Fail(404, "Ontology version not found.");
                if (next.Status != OntologyVersionStatusEnum.Approved) return OntologyResult<SubjectPinResult>.Fail(400, "Only an approved version can be pinned; this version is " + next.Status + ".");
            }
            OntologyVersion? previous = null;
            if (!String.IsNullOrWhiteSpace(subject.OntologyVersionId))
                previous = await _Db.OntologyVersions.ReadAsync(subject.TenantId, subject.OntologyVersionId!, token).ConfigureAwait(false);

            bool taxonomyChanged;
            if (previous == null || next == null) taxonomyChanged = (previous?.Concepts.Count ?? 0) + (next?.Concepts.Count ?? 0) > 0;
            else taxonomyChanged = OntologyDiffer.Compare(previous, next).TaxonomyChanged;

            string? previousId = subject.OntologyVersionId;
            subject.OntologyVersionId = next?.Id;
            Subject saved = await _Db.Subjects.UpdateAsync(subject, token).ConfigureAwait(false);
            return OntologyResult<SubjectPinResult>.Ok(new SubjectPinResult { Subject = saved, PreviousVersionId = previousId, TaxonomyChanged = taxonomyChanged });
        }

        /// <summary>Import a SKOS taxonomy (Turtle or JSON-LD) into a draft's concepts.</summary>
        /// <param name="draft">The draft, with contents loaded.</param>
        /// <param name="document">The SKOS document.</param>
        /// <param name="format">Turtle or JSON-LD.</param>
        /// <param name="mode">Merge into the existing concepts, or replace them.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or 400/409.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft"/> or <paramref name="document"/> is null.</exception>
        public async Task<OntologyResult<TaxonomyImportResult>> ImportTaxonomyAsync(OntologyVersion draft, string document, RdfFormatEnum format, TaxonomyImportModeEnum mode, CancellationToken token = default)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (draft.Status != OntologyVersionStatusEnum.Draft) return OntologyResult<TaxonomyImportResult>.Fail(409, "A taxonomy can only be imported into a draft; this version is " + draft.Status + ".");
            if (String.IsNullOrWhiteSpace(document)) return OntologyResult<TaxonomyImportResult>.Fail(400, "The request body must be a SKOS document.");
            if (document.Length > SkosImporter.MaxDocumentLength) return OntologyResult<TaxonomyImportResult>.Fail(413, "The document is larger than " + (SkosImporter.MaxDocumentLength / (1024 * 1024)) + " MB.");

            TaxonomyImportResult result = new TaxonomyImportResult();
            List<OntologyConcept> imported;
            try
            {
                imported = SkosImporter.Parse(document, format, result.Warnings);
            }
            catch (ArgumentException e)
            {
                return OntologyResult<TaxonomyImportResult>.Fail(400, e.Message);
            }
            if (imported.Count == 0) return OntologyResult<TaxonomyImportResult>.Fail(400, "The document has no skos:Concept with a preferred label.", result.Warnings);

            Dictionary<string, OntologyConcept> byKey = new Dictionary<string, OntologyConcept>(StringComparer.Ordinal);
            if (mode == TaxonomyImportModeEnum.Merge)
            {
                foreach (OntologyConcept concept in draft.Concepts) byKey[concept.Key] = concept;
            }
            else
            {
                result.Removed = draft.Concepts.Count;
            }
            foreach (OntologyConcept concept in imported)
            {
                OntologyConcept? existing;
                if (byKey.TryGetValue(concept.Key, out existing))
                {
                    // Keep what the import cannot know (the node type and case sensitivity chosen in Pneuma).
                    concept.NodeType = existing.NodeType;
                    concept.CaseSensitive = existing.CaseSensitive;
                    result.Updated++;
                }
                else
                {
                    result.Added++;
                }
                byKey[concept.Key] = concept;
            }
            draft.Concepts = byKey.Values.ToList();

            List<string> errors = OntologyVersionValidator.Errors(draft);
            if (errors.Count > 0) return OntologyResult<TaxonomyImportResult>.Fail(400, "The imported taxonomy is not valid: " + String.Join(" ", errors), errors);
            await _Db.OntologyVersions.UpdateAsync(draft, token).ConfigureAwait(false);
            draft.Problems = OntologyVersionValidator.Problems(draft);
            result.Total = draft.Concepts.Count;
            result.Version = draft;
            return OntologyResult<TaxonomyImportResult>.Ok(result);
        }

        /// <summary>The subjects that pin any of a set of versions.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="versionIds">Version identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The pins.</returns>
        public async Task<List<OntologySubjectReference>> PinsAsync(string tenantId, IEnumerable<string> versionIds, CancellationToken token = default)
        {
            HashSet<string> ids = new HashSet<string>(versionIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            List<OntologySubjectReference> pins = new List<OntologySubjectReference>();
            if (ids.Count == 0) return pins;
            List<Subject> subjects = await _Db.Subjects.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            foreach (Subject subject in subjects)
            {
                if (String.IsNullOrWhiteSpace(subject.OntologyVersionId) || !ids.Contains(subject.OntologyVersionId!)) continue;
                pins.Add(new OntologySubjectReference { SubjectId = subject.Id, DisplayName = subject.DisplayName, OntologyVersionId = subject.OntologyVersionId! });
            }
            return pins;
        }

        /// <summary>Copy a version's contents into new lists (rules get new ids, so the copy can be stored beside the original).</summary>
        /// <param name="source">The source version, or null for empty contents.</param>
        /// <param name="target">The version to fill.</param>
        public static void CopyContents(OntologyVersion? source, OntologyVersion target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (source == null) return;
            target.Guidance = source.Guidance;
            target.UndeclaredTypeAction = source.UndeclaredTypeAction;
            target.NodeTypes = source.NodeTypes.Select(t => new OntologyNodeType { Name = t.Name, Description = t.Description }).ToList();
            target.EdgeTypes = source.EdgeTypes.Select(t => new OntologyEdgeType { Name = t.Name, Description = t.Description }).ToList();
            target.Rules = source.Rules.Select(r => new OntologyRule
            {
                RuleType = r.RuleType, NodeType = r.NodeType, EdgeType = r.EdgeType, FromNodeType = r.FromNodeType, ToNodeType = r.ToNodeType,
                Field = r.Field, Pattern = r.Pattern, MaxCount = r.MaxCount, MinConfidence = r.MinConfidence, Action = r.Action, Description = r.Description
            }).ToList();
            target.Concepts = source.Concepts.Select(c => new OntologyConcept
            {
                Key = c.Key, PrefLabel = c.PrefLabel, AltLabels = new List<string>(c.AltLabels), BroaderKey = c.BroaderKey,
                Definition = c.Definition, NodeType = c.NodeType, CaseSensitive = c.CaseSensitive
            }).ToList();
        }

        #endregion

        #region Private-Methods

        private static OntologyVersion NewDraft(TenantOntology ontology, int number, OntologyVersion? source, string? userId)
        {
            OntologyVersion draft = new OntologyVersion
            {
                TenantId = ontology.TenantId,
                OntologyId = ontology.Id,
                VersionNumber = number,
                Status = OntologyVersionStatusEnum.Draft,
                BasedOnVersionId = String.IsNullOrEmpty(source?.TenantId) ? null : source!.Id,
                CreatedByUserId = userId
            };
            CopyContents(source, draft);
            return draft;
        }

        private static string? CheckName(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "An ontology name is required.";
            if (name!.Trim().Length > 256) return "An ontology name can be at most 256 characters.";
            return null;
        }

        #endregion
    }
}
