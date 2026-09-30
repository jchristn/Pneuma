namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Keys and default contents of the new subject wizard's prompts. Each step has a task prompt and an output format
    /// prompt (the JSON shape the parser expects). They are seeded as system prompts and can be overridden per tenant.
    /// </summary>
    public static class SubjectWizardPrompts
    {
        #region Public-Members

        /// <summary>Brief step task.</summary>
        public const string BriefKey = "wizard.brief";

        /// <summary>Brief step output format.</summary>
        public const string BriefFormatKey = "wizard.brief.format";

        /// <summary>Questions step task.</summary>
        public const string QuestionsKey = "wizard.questions";

        /// <summary>Questions step output format.</summary>
        public const string QuestionsFormatKey = "wizard.questions.format";

        /// <summary>Ontology step task.</summary>
        public const string OntologyKey = "wizard.ontology";

        /// <summary>Ontology step output format.</summary>
        public const string OntologyFormatKey = "wizard.ontology.format";

        /// <summary>Prompts step task.</summary>
        public const string PromptsKey = "wizard.prompts";

        /// <summary>Prompts step output format.</summary>
        public const string PromptsFormatKey = "wizard.prompts.format";

        /// <summary>Sources step task.</summary>
        public const string SourcesKey = "wizard.sources";

        /// <summary>Sources step output format.</summary>
        public const string SourcesFormatKey = "wizard.sources.format";

        /// <summary>Default brief task.</summary>
        public const string DefaultBrief =
            "You help someone set up a knowledge archive about one subject. From their description (and any reference text), " +
            "write a short brief: the subject's proper display name, a free-text type (for example Musician, Medical device company, " +
            "City council, Research field), a one-paragraph factual description, a one-line tagline for the page where people ask " +
            "questions, the audience who will ask, and the tone answers should take for that audience. Stay close to what the " +
            "description says; do not invent facts about the subject. If the description is ambiguous, choose the most likely " +
            "meaning and say so in the description.";

        /// <summary>Default brief output format.</summary>
        public const string DefaultBriefFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"displayName\":\"...\",\"type\":\"...\"," +
            "\"description\":\"...\",\"tagline\":\"...\",\"audience\":\"...\",\"tone\":\"...\"}. Keep the tagline under 80 characters " +
            "and the description under 600 characters.";

        /// <summary>Default questions task.</summary>
        public const string DefaultQuestions =
            "You help someone set up a knowledge archive about one subject. Propose the questions its audience is most likely to " +
            "ask. Cover a spread of kinds: fact (who, what, when, where), relationship (how people, works, organizations, and " +
            "things connect), timeline (how something changed or happened over time), comparison, reasoning (why or how, needing " +
            "several facts), and overview (a broad summary of a theme). Write each question the way a real person in the audience " +
            "would type it, specific to this subject, answerable from documents about it. Do not repeat or rephrase questions " +
            "already chosen.";

        /// <summary>Default questions output format.</summary>
        public const string DefaultQuestionsFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"questions\":[{\"question\":\"...\"," +
            "\"kind\":\"fact\"}]}. kind is one of fact, relationship, timeline, comparison, reasoning, overview.";

        /// <summary>Default ontology task.</summary>
        public const string DefaultOntology =
            "You are a knowledge engineer designing the ontology for a knowledge graph about one subject. The graph must be able to " +
            "answer the listed questions. Start from the built-in types: keep the ones this subject needs, drop the ones it does not, " +
            "and add domain types where a built-in type is too general to answer a question well. Reuse a built-in name whenever the " +
            "meaning is the same (a musician or author is a Person; a song or book is a Work). Types are kinds of things, never one " +
            "specific thing: the subject itself is the Subject type, and a particular person, work, or place is an instance, not a type. Prefer a small set (typically 6 to 15 node types and 6 to 20 relationship types). Give every type a " +
            "one-sentence description a classifier can apply consistently, and list the numbers of the questions it helps answer. " +
            "Every relationship names the node type it starts from and the one it points to. Keep every type marked as chosen exactly " +
            "as given. Also write short extraction guidance: what a classifier should pay attention to in documents about this subject.";

        /// <summary>Default ontology output format.</summary>
        public const string DefaultOntologyFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"nodeTypes\":[{\"name\":\"Person\"," +
            "\"description\":\"...\",\"questions\":[1,4]}],\"edgeTypes\":[{\"name\":\"PERFORMED_ON\",\"description\":\"...\"," +
            "\"from\":\"Person\",\"to\":\"Recording\",\"questions\":[2]}],\"guidance\":\"...\"}. Name node types as singular nouns in " +
            "PascalCase and relationship types as verbs in UPPER_SNAKE_CASE. from and to must be node type names you list. questions " +
            "holds question numbers as integers.";

        /// <summary>Default prompts task.</summary>
        public const string DefaultPrompts =
            "You write the subject-specific additions to four prompts of a question-answering archive. Each addition is appended to a " +
            "general prompt that already explains the task and the output format, so write only what is specific to this subject, in " +
            "two to six sentences each. systemPrompt: how answers should sound for this audience, which conventions to follow (for " +
            "example citing titles and years), and what to say when the archive does not support an answer. classifyPrompt: what to " +
            "extract from documents about this subject so the questions can be answered, using the ontology's type names, including " +
            "domain hints such as which things count as which type. rewritePrompt: synonyms, nicknames, abbreviations, and alternate " +
            "spellings a search should expand. rerankingPrompt: what makes a passage most useful to this audience. Keep every prompt " +
            "marked as chosen exactly as given.";

        /// <summary>Default prompts output format.</summary>
        public const string DefaultPromptsFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"systemPrompt\":\"...\",\"classifyPrompt\":\"...\"," +
            "\"rewritePrompt\":\"...\",\"rerankingPrompt\":\"...\"}.";

        /// <summary>Default sources task.</summary>
        public const string DefaultSources =
            "You help someone fill a new knowledge archive with content. Suggest three to six places where good content about this " +
            "subject usually lives, specific to its type and audience (for example an official site, a discography, published papers, " +
            "a documentation site, a code repository, a shared drive). For each, say how it would be added: Links for a few pages, " +
            "Sitemap or Web for a whole site, GitHub for a repository, S3, AzureBlob, or GoogleCloud for a storage bucket, Cifs or Nfs " +
            "for a file share, or Text for notes typed or pasted in. Do not invent specific URLs.";

        /// <summary>Default sources output format.</summary>
        public const string DefaultSourcesFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"suggestions\":[{\"kind\":\"Sitemap\"," +
            "\"title\":\"...\",\"detail\":\"...\"}]}. kind is one of Links, Text, Web, Sitemap, GitHub, S3, AzureBlob, GoogleCloud, Cifs, Nfs.";

        #endregion

        #region Public-Methods

        /// <summary>All wizard prompts: key, display name, and default content.</summary>
        /// <returns>The prompts, task before format for each step.</returns>
        public static List<SubjectWizardPromptDefault> All()
        {
            return new List<SubjectWizardPromptDefault>
            {
                new SubjectWizardPromptDefault { Key = BriefKey, Name = "Subject Wizard: Brief", Content = DefaultBrief },
                new SubjectWizardPromptDefault { Key = BriefFormatKey, Name = "Subject Wizard: Brief Output Format", Content = DefaultBriefFormat },
                new SubjectWizardPromptDefault { Key = QuestionsKey, Name = "Subject Wizard: Example Questions", Content = DefaultQuestions },
                new SubjectWizardPromptDefault { Key = QuestionsFormatKey, Name = "Subject Wizard: Example Questions Output Format", Content = DefaultQuestionsFormat },
                new SubjectWizardPromptDefault { Key = OntologyKey, Name = "Subject Wizard: Ontology", Content = DefaultOntology },
                new SubjectWizardPromptDefault { Key = OntologyFormatKey, Name = "Subject Wizard: Ontology Output Format", Content = DefaultOntologyFormat },
                new SubjectWizardPromptDefault { Key = PromptsKey, Name = "Subject Wizard: Prompts", Content = DefaultPrompts },
                new SubjectWizardPromptDefault { Key = PromptsFormatKey, Name = "Subject Wizard: Prompts Output Format", Content = DefaultPromptsFormat },
                new SubjectWizardPromptDefault { Key = SourcesKey, Name = "Subject Wizard: Source Suggestions", Content = DefaultSources },
                new SubjectWizardPromptDefault { Key = SourcesFormatKey, Name = "Subject Wizard: Source Suggestions Output Format", Content = DefaultSourcesFormat }
            };
        }

        #endregion
    }
}
