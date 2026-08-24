namespace Pneuma.Core.Graph
{
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

        #endregion

    }
}
