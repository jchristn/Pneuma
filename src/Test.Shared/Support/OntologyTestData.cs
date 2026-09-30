namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ontologies;

    /// <summary>Ontology versions and classifier replies shared by the ontology suites.</summary>
    public static class OntologyTestData
    {
        /// <summary>
        /// A small governed version: Person, Organization, Work, and Topic; WORKS_FOR (Person to Organization, reversible),
        /// CREATED_BY (Work to Person, dropped otherwise), and ABOUT; at most one CREATED_BY per Work (quarantined); a
        /// Person needs content (warn); WORKS_FOR needs confidence 0.5 (drop); undeclared types are quarantined; and a
        /// two-level taxonomy (Cloud computing, with Kubernetes under it, alt label k8s).
        /// </summary>
        /// <returns>The version contents (no tenant or ontology yet).</returns>
        public static OntologyVersion Governed()
        {
            OntologyVersion version = new OntologyVersion
            {
                Guidance = "Prefer Organization for named teams.",
                UndeclaredTypeAction = UndeclaredTypeActionEnum.Quarantine
            };
            version.NodeTypes.Add(new OntologyNodeType { Name = "Person", Description = "A named individual." });
            version.NodeTypes.Add(new OntologyNodeType { Name = "Organization", Description = "A company or team." });
            version.NodeTypes.Add(new OntologyNodeType { Name = "Work", Description = "A created work." });
            version.NodeTypes.Add(new OntologyNodeType { Name = "Topic", Description = "A theme." });
            version.EdgeTypes.Add(new OntologyEdgeType { Name = "WORKS_FOR", Description = "Employment." });
            version.EdgeTypes.Add(new OntologyEdgeType { Name = "CREATED_BY", Description = "Authorship." });
            version.EdgeTypes.Add(new OntologyEdgeType { Name = "ABOUT", Description = "Subject matter." });
            version.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.EdgeEndpoints, EdgeType = "WORKS_FOR", FromNodeType = "Person", ToNodeType = "Organization", Action = OntologyRuleActionEnum.Reverse });
            version.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.EdgeEndpoints, EdgeType = "CREATED_BY", FromNodeType = "Work", ToNodeType = "Person", Action = OntologyRuleActionEnum.Drop });
            version.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.MaxOutgoing, NodeType = "Work", EdgeType = "CREATED_BY", MaxCount = 1, Action = OntologyRuleActionEnum.Quarantine });
            version.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.RequiredField, NodeType = "Person", Field = OntologyNodeFieldEnum.Content, Action = OntologyRuleActionEnum.Warn });
            version.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.MinConfidence, EdgeType = "WORKS_FOR", MinConfidence = 0.5, Action = OntologyRuleActionEnum.Drop });
            version.Concepts.Add(new OntologyConcept { Key = "cloud", PrefLabel = "Cloud computing", NodeType = "Topic" });
            version.Concepts.Add(new OntologyConcept { Key = "k8s", PrefLabel = "Kubernetes", AltLabels = new List<string> { "k8s", "K8S cluster" }, BroaderKey = "cloud", NodeType = "Topic" });
            return version;
        }

        /// <summary>
        /// A classifier reply that exercises every rule of <see cref="Governed"/>: Ada (no content, warned), Acme, a Work
        /// with two creators (one quarantined), a WORKS_FOR written backwards (reversed), a weak WORKS_FOR (dropped), a
        /// CREATED_BY from a Work to an Organization (dropped), and an undeclared Planet (quarantined).
        /// </summary>
        public const string GovernedReply =
            "{\"nodes\":[" +
            "{\"ref\":\"p1\",\"nodeType\":\"person\",\"name\":\"Ada\",\"confidence\":0.9}," +
            "{\"ref\":\"p2\",\"nodeType\":\"Person\",\"name\":\"Grace\",\"content\":\"A pioneer.\",\"confidence\":0.9}," +
            "{\"ref\":\"o1\",\"nodeType\":\"Organization\",\"name\":\"Acme\",\"confidence\":0.9}," +
            "{\"ref\":\"w1\",\"nodeType\":\"Work\",\"name\":\"The Book\",\"confidence\":0.9}," +
            "{\"ref\":\"x1\",\"nodeType\":\"Planet\",\"name\":\"Mars\",\"confidence\":0.9}]," +
            "\"edges\":[" +
            "{\"fromRef\":\"o1\",\"toRef\":\"p2\",\"edgeType\":\"works for\",\"confidence\":0.9}," +
            "{\"fromRef\":\"p1\",\"toRef\":\"o1\",\"edgeType\":\"WORKS_FOR\",\"confidence\":0.2}," +
            "{\"fromRef\":\"w1\",\"toRef\":\"p1\",\"edgeType\":\"CREATED_BY\",\"confidence\":0.9}," +
            "{\"fromRef\":\"w1\",\"toRef\":\"p2\",\"edgeType\":\"CREATED_BY\",\"confidence\":0.4}," +
            "{\"fromRef\":\"w1\",\"toRef\":\"o1\",\"edgeType\":\"CREATED_BY\",\"confidence\":0.9}]}";

        /// <summary>A small SKOS taxonomy in Turtle.</summary>
        public const string SkosTurtle =
            "@prefix skos: <http://www.w3.org/2004/02/skos/core#> .\n" +
            "<http://example.org/c/databases> a skos:Concept ; skos:prefLabel \"Databases\"@en ; skos:altLabel \"DBMS\" .\n" +
            "<http://example.org/c/postgres> a skos:Concept ; skos:prefLabel \"PostgreSQL\" ; skos:altLabel \"Postgres\" ; skos:broader <http://example.org/c/databases> ; skos:definition \"An open-source database.\" .\n" +
            "<http://example.org/c/unnamed> a skos:Concept .\n";
    }
}
