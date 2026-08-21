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
        /// <summary>A person: collaborator, producer, influence.</summary>
        public const string NodePerson = "Person";
        /// <summary>An organization: label, band, venue-as-org, media outlet.</summary>
        public const string NodeOrganization = "Organization";
        /// <summary>Container for a subject's released body of work.</summary>
        public const string NodeDiscography = "Discography";
        /// <summary>An album, EP, or single.</summary>
        public const string NodeRecord = "Record";
        /// <summary>A song.</summary>
        public const string NodeTrack = "Track";
        /// <summary>Lyric content for a track.</summary>
        public const string NodeLyrics = "Lyrics";
        /// <summary>A generic creative work (book, artwork, essay).</summary>
        public const string NodeWork = "Work";
        /// <summary>A concert, interview, broadcast, or appearance.</summary>
        public const string NodeEvent = "Event";
        /// <summary>A place or venue.</summary>
        public const string NodePlace = "Place";
        /// <summary>A topic or motif.</summary>
        public const string NodeTheme = "Theme";
        /// <summary>A historical or cultural context node.</summary>
        public const string NodeCulturalMoment = "CulturalMoment";
        /// <summary>A provenance anchor: the artifact a claim came from.</summary>
        public const string NodeSource = "Source";

        /// <summary>A semantic cell of a source document, carrying its extracted text. Cells are the
        /// graph's unit of source content; their finer-grained chunks live only in RecallDB.</summary>
        public const string NodeCell = "Cell";
        /// <summary>A chunk of a source document. Legacy node type — chunks are no longer stored in the
        /// graph (they live only in RecallDB); retained so any pre-existing chunk nodes still resolve.</summary>
        public const string NodeChunk = "Chunk";
        /// <summary>A retrievable media asset.</summary>
        public const string NodeMedia = "Media";

        #endregion

        #region Edge-Types

        /// <summary>Subject has a discography.</summary>
        public const string EdgeHasDiscography = "HAS_DISCOGRAPHY";
        /// <summary>Discography contains a record.</summary>
        public const string EdgeContainsRecord = "CONTAINS_RECORD";
        /// <summary>Record has a track.</summary>
        public const string EdgeHasTrack = "HAS_TRACK";
        /// <summary>Track has lyrics.</summary>
        public const string EdgeHasLyrics = "HAS_LYRICS";
        /// <summary>Work performed by a person/subject.</summary>
        public const string EdgePerformedBy = "PERFORMED_BY";
        /// <summary>Work produced by a person.</summary>
        public const string EdgeProducedBy = "PRODUCED_BY";
        /// <summary>Collaboration between people.</summary>
        public const string EdgeCollaboratedWith = "COLLABORATED_WITH";
        /// <summary>Membership in an organization.</summary>
        public const string EdgeMemberOf = "MEMBER_OF";
        /// <summary>Released on a label/organization.</summary>
        public const string EdgeReleasedOn = "RELEASED_ON";
        /// <summary>Event performed at a place.</summary>
        public const string EdgePerformedAt = "PERFORMED_AT";
        /// <summary>Event occurred on a date/moment.</summary>
        public const string EdgeOccurredOn = "OCCURRED_ON";
        /// <summary>Entity is about a theme.</summary>
        public const string EdgeAboutTheme = "ABOUT_THEME";
        /// <summary>References a cultural moment.</summary>
        public const string EdgeReferencesMoment = "REFERENCES_MOMENT";
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
