namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Built-in ontology templates. They are product content in code (system scope, read-only): a tenant creates its own
    /// ontology from a template and edits its copy, so no tenant can change what another tenant starts from.
    /// </summary>
    public static class OntologyTemplates
    {
        #region Public-Members

        /// <summary>Name of the built-in, domain-neutral template (the same node and edge types Pneuma uses without an ontology).</summary>
        public const string Default = "Default";

        #endregion

        #region Public-Methods

        /// <summary>List the built-in templates.</summary>
        /// <returns>Template descriptions.</returns>
        public static List<OntologyTemplateInfo> List()
        {
            OntologyVersion content = Build(Default) ?? new OntologyVersion();
            return new List<OntologyTemplateInfo>
            {
                new OntologyTemplateInfo
                {
                    Name = Default,
                    Description = "Domain-neutral types for any kind of subject: people, organizations, works, collections, events, places, and topics, with the relationships between them. The same types Pneuma uses when a subject has no ontology.",
                    NodeTypeCount = content.NodeTypes.Count,
                    EdgeTypeCount = content.EdgeTypes.Count,
                    RuleCount = content.Rules.Count
                }
            };
        }

        /// <summary>Build a template's contents as an unsaved version (no tenant, ontology, or number yet).</summary>
        /// <param name="name">Template name (case-insensitive).</param>
        /// <returns>The contents, or null when no template has the name.</returns>
        public static OntologyVersion? Build(string? name)
        {
            if (!String.Equals(name, Default, StringComparison.OrdinalIgnoreCase)) return null;

            OntologyVersion version = new OntologyVersion
            {
                Guidance = "When information is present but does not fit a listed type, prefer the closest listed type rather than inventing an unrelated one.",
                UndeclaredTypeAction = UndeclaredTypeActionEnum.Allow
            };

            AddNode(version, Ontology.NodeSubject, "The subject the archive is about.");
            AddNode(version, Ontology.NodePerson, "An individual: a collaborator, contributor, official, influence, or other named person.");
            AddNode(version, Ontology.NodeOrganization, "A company, institution, group, team, agency, publisher, or other named body.");
            AddNode(version, Ontology.NodeWork, "A discrete created or published work: a document, article, book, report, product, release, recording, film, dataset, or artwork.");
            AddNode(version, Ontology.NodeCollection, "A container that groups related works (a series, catalog, product line, or body of work).");
            AddNode(version, Ontology.NodeEvent, "Something that happened at a point in time: a meeting, release, announcement, incident, or milestone.");
            AddNode(version, Ontology.NodePlace, "A location: a city, region, address, venue, or facility.");
            AddNode(version, Ontology.NodeTopic, "A recurring theme, concept, subject-matter area, or motif.");
            AddNode(version, Ontology.NodeMedia, "A retrievable media asset (audio, video, image, or document).");

            AddEdge(version, Ontology.EdgeHasPart, "Containment or composition: a collection contains a work, or a work has a part.");
            AddEdge(version, Ontology.EdgeCreatedBy, "A work was created or authored by a person or organization.");
            AddEdge(version, Ontology.EdgeContributedTo, "A person or organization contributed to a work.");
            AddEdge(version, Ontology.EdgePublishedBy, "A work or collection was published or released by an organization.");
            AddEdge(version, Ontology.EdgeAffiliatedWith, "A person is affiliated with an organization (membership, employment, role).");
            AddEdge(version, Ontology.EdgeCollaboratedWith, "Collaboration between people.");
            AddEdge(version, Ontology.EdgeLocatedAt, "An event or organization is located at a place.");
            AddEdge(version, Ontology.EdgeOccurredOn, "An event occurred on a date or in relation to another event.");
            AddEdge(version, Ontology.EdgeAbout, "An entity is about a topic.");
            AddEdge(version, Ontology.EdgeInfluencedBy, "One entity was influenced by another.");
            AddEdge(version, Ontology.EdgeHasMedia, "An entity has an associated media asset.");
            AddEdge(version, Ontology.EdgeMentions, "One entity mentions another.");

            AddEndpoints(version, Ontology.EdgeHasPart, Ontology.NodeCollection, Ontology.NodeWork);
            AddEndpoints(version, Ontology.EdgeHasPart, Ontology.NodeWork, Ontology.NodeWork);
            AddEndpoints(version, Ontology.EdgeCreatedBy, Ontology.NodeWork, Ontology.NodePerson);
            AddEndpoints(version, Ontology.EdgeCreatedBy, Ontology.NodeWork, Ontology.NodeOrganization);
            AddEndpoints(version, Ontology.EdgeContributedTo, Ontology.NodePerson, Ontology.NodeWork);
            AddEndpoints(version, Ontology.EdgeContributedTo, Ontology.NodeOrganization, Ontology.NodeWork);
            AddEndpoints(version, Ontology.EdgePublishedBy, Ontology.NodeWork, Ontology.NodeOrganization);
            AddEndpoints(version, Ontology.EdgePublishedBy, Ontology.NodeCollection, Ontology.NodeOrganization);
            AddEndpoints(version, Ontology.EdgeAffiliatedWith, Ontology.NodePerson, Ontology.NodeOrganization);
            AddEndpoints(version, Ontology.EdgeCollaboratedWith, Ontology.NodePerson, Ontology.NodePerson);
            AddEndpoints(version, Ontology.EdgeLocatedAt, Ontology.NodeEvent, Ontology.NodePlace);
            AddEndpoints(version, Ontology.EdgeLocatedAt, Ontology.NodeOrganization, Ontology.NodePlace);
            return version;
        }

        #endregion

        #region Private-Methods

        private static void AddNode(OntologyVersion version, string name, string description)
        {
            version.NodeTypes.Add(new OntologyNodeType { Name = name, Description = description });
        }

        private static void AddEdge(OntologyVersion version, string name, string description)
        {
            version.EdgeTypes.Add(new OntologyEdgeType { Name = name, Description = description });
        }

        // Endpoint rules in the template only warn: they describe the intended shape without discarding anything, so a
        // tenant can tighten them to Reverse, Drop, or Quarantine once it has seen how its content classifies.
        private static void AddEndpoints(OntologyVersion version, string edgeType, string from, string to)
        {
            version.Rules.Add(new OntologyRule
            {
                RuleType = OntologyRuleTypeEnum.EdgeEndpoints,
                EdgeType = edgeType,
                FromNodeType = from,
                ToNodeType = to,
                Action = OntologyRuleActionEnum.Warn
            });
        }

        #endregion
    }
}
