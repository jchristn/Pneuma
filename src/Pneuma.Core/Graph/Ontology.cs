namespace Pneuma.Core.Graph
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// The Pneuma knowledge-graph ontology for subject archives (subject-neutral, supporting any kind of
    /// subject). Node and edge types are LiteGraph labels; cross-cutting metadata (provenance,
    /// rights, authority, confidence) is carried as tags.
    /// </summary>
    public static class Ontology
    {
        #region Node-Types

        /// <summary>The subject the archive is about.</summary>
        public const string NodeSubject = "Subject";
        /// <summary>An individual: a collaborator, contributor, official, influence, or other named person.</summary>
        public const string NodePerson = "Person";
        /// <summary>An organization: a company, institution, group, team, agency, publisher, or other named body.</summary>
        public const string NodeOrganization = "Organization";
        /// <summary>A discrete created or published work: a document, article, book, report, product, release,
        /// recording, film, dataset, or artwork.</summary>
        public const string NodeWork = "Work";
        /// <summary>A container that groups related works (a series, catalog, product line, or body of work).</summary>
        public const string NodeCollection = "Collection";
        /// <summary>Something that happened at a point in time: a meeting, release, announcement, incident, or milestone.</summary>
        public const string NodeEvent = "Event";
        /// <summary>A location: a city, region, address, venue, or facility.</summary>
        public const string NodePlace = "Place";
        /// <summary>A recurring theme, concept, subject-matter area, or motif.</summary>
        public const string NodeTopic = "Topic";
        /// <summary>A provenance anchor: the artifact a claim came from.</summary>
        public const string NodeSource = "Source";

        /// <summary>A semantic cell of a source document, carrying its extracted text. Cells are the
        /// graph's unit of source content; their finer-grained chunks live only in RecallDB.</summary>
        public const string NodeCell = "Cell";
        /// <summary>A chunk of a source document. Legacy node type — chunks are no longer stored in the
        /// graph (they live only in RecallDB); retained so any pre-existing chunk nodes still resolve.</summary>
        public const string NodeChunk = "Chunk";
        /// <summary>A retrievable media asset (audio, video, image, or document).</summary>
        public const string NodeMedia = "Media";
        /// <summary>An LLM-generated summary of a detected community (a GraphRAG "community report"), stored as a
        /// node so it is co-located with the graph and cascade-cleaned with its subject.</summary>
        public const string NodeCommunitySummary = "CommunitySummary";

        #endregion

        #region Edge-Types

        /// <summary>Containment or composition: a collection contains a work, or a work has a part (container -&gt; part).</summary>
        public const string EdgeHasPart = "HAS_PART";
        /// <summary>A work was created/authored by a person or organization.</summary>
        public const string EdgeCreatedBy = "CREATED_BY";
        /// <summary>A person or organization contributed to a work.</summary>
        public const string EdgeContributedTo = "CONTRIBUTED_TO";
        /// <summary>A work or collection was published/released by an organization.</summary>
        public const string EdgePublishedBy = "PUBLISHED_BY";
        /// <summary>A person is affiliated with an organization (membership, employment, role).</summary>
        public const string EdgeAffiliatedWith = "AFFILIATED_WITH";
        /// <summary>Collaboration between people.</summary>
        public const string EdgeCollaboratedWith = "COLLABORATED_WITH";
        /// <summary>An event or organization is located at a place.</summary>
        public const string EdgeLocatedAt = "LOCATED_AT";
        /// <summary>An event occurred on a date or in relation to another event.</summary>
        public const string EdgeOccurredOn = "OCCURRED_ON";
        /// <summary>An entity is about a topic.</summary>
        public const string EdgeAbout = "ABOUT";
        /// <summary>Influenced by another entity.</summary>
        public const string EdgeInfluencedBy = "INFLUENCED_BY";
        /// <summary>Derived from a provenance source.</summary>
        public const string EdgeDerivedFromSource = "DERIVED_FROM_SOURCE";

        /// <summary>Links a source node to one of its cell nodes.</summary>
        public const string EdgeHasCell = "HAS_CELL";
        /// <summary>Links a source node to one of its chunk nodes. Legacy — chunks are no longer graph nodes.</summary>
        public const string EdgeHasChunk = "HAS_CHUNK";
        /// <summary>Has an associated media asset.</summary>
        public const string EdgeHasMedia = "HAS_MEDIA";
        /// <summary>Mentions another entity (entity-resolution link).</summary>
        public const string EdgeMentions = "MENTIONS";

        #endregion

        #region Tag-Keys

        /// <summary>Tenant identifier tag.</summary>
        public const string TagTenantId = "tenantId";
        /// <summary>Subject identifier tag.</summary>
        public const string TagSubjectId = "subjectId";
        /// <summary>Node type tag (mirrors the primary label for querying).</summary>
        public const string TagNodeType = "nodeType";
        /// <summary>Canonical name tag used for entity resolution.</summary>
        public const string TagCanonicalName = "canonicalName";
        /// <summary>Provenance source node identifier tag.</summary>
        public const string TagSourceId = "sourceId";
        /// <summary>Rights classification tag.</summary>
        public const string TagRights = "rights";
        /// <summary>Authority/trust classification tag.</summary>
        public const string TagAuthority = "authority";
        /// <summary>Model confidence tag (0..1).</summary>
        public const string TagConfidence = "confidence";
        /// <summary>Ingestion job identifier that asserted the element.</summary>
        public const string TagAssertedByJob = "assertedByJob";
        /// <summary>Consolidated relationship weight (0..1), accumulated across corroborating assertions (noisy-OR).</summary>
        public const string TagWeight = "weight";
        /// <summary>How many times a relationship has been asserted/corroborated across sources.</summary>
        public const string TagCorroborationCount = "corroborations";
        /// <summary>Community identifier tag on a community-summary node.</summary>
        public const string TagCommunityId = "communityId";
        /// <summary>Number of member entities a community summary was built from.</summary>
        public const string TagMemberCount = "memberCount";

        #endregion

        #region Vocabulary-Canonicalization

        // Node/edge types are matched by a normalized key (uppercased, non-alphanumerics stripped) so casing,
        // spacing, and punctuation variance all resolve to the same built-in constant. A tiny alias map covers
        // spellings that do not normalize to a built-in (e.g. the British "Organisation"). The ontology is
        // admin-defined in natural language, so a type that matches nothing built-in is NOT rejected — it is
        // only lightly canonicalized (whitespace-collapsed for nodes, UPPER_SNAKE for edges) so the same
        // emitted concept always yields the same label instead of proliferating variants.
        private static readonly Dictionary<string, string> _NodeTypeByKey = BuildNodeIndex();
        private static readonly Dictionary<string, string> _EdgeTypeByKey = BuildEdgeIndex();

        /// <summary>
        /// Canonicalize a model-emitted node type: coerce it to the matching built-in ontology type when one is
        /// recognized (case/spacing/punctuation-insensitive, plus a small alias map), otherwise return the
        /// trimmed, whitespace-collapsed value unchanged so admin-defined custom types are preserved.
        /// </summary>
        /// <param name="raw">The raw node type emitted by classification.</param>
        /// <returns>The canonical node type.</returns>
        public static string CanonicalNodeType(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return raw ?? String.Empty;
            string trimmed = raw!.Trim();
            string key = NormalizeKey(trimmed);
            if (key.Length > 0 && _NodeTypeByKey.TryGetValue(key, out string? canonical)) return canonical;
            return CollapseWhitespace(trimmed);
        }

        /// <summary>
        /// Canonicalize a model-emitted edge type: coerce it to the matching built-in relationship type when one
        /// is recognized, otherwise return an UPPER_SNAKE_CASE normalization (edges are conventionally
        /// upper-snake), so custom relationships are preserved but do not drift on casing/spacing.
        /// </summary>
        /// <param name="raw">The raw edge type emitted by classification.</param>
        /// <returns>The canonical edge type.</returns>
        public static string CanonicalEdgeType(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return raw ?? String.Empty;
            string trimmed = raw!.Trim();
            string key = NormalizeKey(trimmed);
            if (key.Length > 0 && _EdgeTypeByKey.TryGetValue(key, out string? canonical)) return canonical;
            return ToUpperSnake(trimmed);
        }

        private static Dictionary<string, string> BuildNodeIndex()
        {
            string[] types = { NodeSubject, NodePerson, NodeOrganization, NodeWork, NodeCollection, NodeEvent, NodePlace, NodeTopic, NodeSource, NodeCell, NodeChunk, NodeMedia };
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string type in types) index[NormalizeKey(type)] = type;
            // Spellings that do not normalize to a built-in key.
            index[NormalizeKey("Organisation")] = NodeOrganization;
            return index;
        }

        private static Dictionary<string, string> BuildEdgeIndex()
        {
            string[] types =
            {
                EdgeHasPart, EdgeCreatedBy, EdgeContributedTo, EdgePublishedBy, EdgeAffiliatedWith, EdgeCollaboratedWith,
                EdgeLocatedAt, EdgeOccurredOn, EdgeAbout, EdgeInfluencedBy, EdgeDerivedFromSource, EdgeHasCell, EdgeHasChunk,
                EdgeHasMedia, EdgeMentions
            };
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string type in types) index[NormalizeKey(type)] = type;
            return index;
        }

        /// <summary>Uppercase and strip every non-alphanumeric character, producing a match key.</summary>
        private static string NormalizeKey(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (Char.IsLetterOrDigit(c)) builder.Append(Char.ToUpperInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>Trim and collapse internal runs of whitespace to a single space.</summary>
        private static string CollapseWhitespace(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            bool inSpace = false;
            foreach (char c in value.Trim())
            {
                if (Char.IsWhiteSpace(c))
                {
                    inSpace = true;
                    continue;
                }
                if (inSpace && builder.Length > 0) builder.Append(' ');
                inSpace = false;
                builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>Normalize to UPPER_SNAKE_CASE: uppercase, non-alphanumeric runs become a single underscore.</summary>
        private static string ToUpperSnake(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            bool pendingUnderscore = false;
            foreach (char c in value.Trim())
            {
                if (Char.IsLetterOrDigit(c))
                {
                    if (pendingUnderscore && builder.Length > 0) builder.Append('_');
                    pendingUnderscore = false;
                    builder.Append(Char.ToUpperInvariant(c));
                }
                else
                {
                    pendingUnderscore = true;
                }
            }
            return builder.ToString();
        }

        #endregion

    }
}
