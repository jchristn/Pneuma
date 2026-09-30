namespace Pneuma.Core.Wizard
{
    using System;

    /// <summary>Server limits for the new subject wizard (the <c>Wizard</c> section of pneuma.json).</summary>
    public class WizardSettings
    {
        #region Public-Members

        /// <summary>Questions drafted when a request does not say. Default 12; clamped to [3, 40].</summary>
        public int DefaultQuestionCount
        {
            get { return _DefaultQuestionCount; }
            set { _DefaultQuestionCount = Math.Clamp(value, 3, 40); }
        }

        /// <summary>Most questions a draft can hold. Default 40; clamped to [5, 100].</summary>
        public int MaxQuestions
        {
            get { return _MaxQuestions; }
            set { _MaxQuestions = Math.Clamp(value, 5, 100); }
        }

        /// <summary>Most node types plus relationship types an ontology draft can hold. Default 60; clamped to [10, 200].</summary>
        public int MaxOntologyTypes
        {
            get { return _MaxOntologyTypes; }
            set { _MaxOntologyTypes = Math.Clamp(value, 10, 200); }
        }

        /// <summary>Most characters of grounding text sent to the model, shared between the reference pages. Default 6000 (small local models have short contexts); clamped to [1000, 100000].</summary>
        public int MaxGroundingCharacters
        {
            get { return _MaxGroundingCharacters; }
            set { _MaxGroundingCharacters = Math.Clamp(value, 1000, 100000); }
        }

        /// <summary>Most reference URLs the brief step reads. Default 5; clamped to [1, 20].</summary>
        public int MaxGroundingUrls
        {
            get { return _MaxGroundingUrls; }
            set { _MaxGroundingUrls = Math.Clamp(value, 1, 20); }
        }

        /// <summary>Most characters in one drafted prompt addition. Default 4000; clamped to [500, 20000].</summary>
        public int MaxPromptCharacters
        {
            get { return _MaxPromptCharacters; }
            set { _MaxPromptCharacters = Math.Clamp(value, 500, 20000); }
        }

        /// <summary>Seconds one model call may take. Default 300 (small local models need minutes for the ontology); clamped to [10, 900].</summary>
        public int TimeoutSeconds
        {
            get { return _TimeoutSeconds; }
            set { _TimeoutSeconds = Math.Clamp(value, 10, 900); }
        }

        /// <summary>Most questions the coverage check asks in one run. Default 12; clamped to [1, 50].</summary>
        public int CoverageMaxQuestions
        {
            get { return _CoverageMaxQuestions; }
            set { _CoverageMaxQuestions = Math.Clamp(value, 1, 50); }
        }

        #endregion

        #region Private-Members

        private int _DefaultQuestionCount = 12;
        private int _MaxQuestions = 40;
        private int _MaxOntologyTypes = 60;
        private int _MaxGroundingCharacters = 6000;
        private int _MaxPromptCharacters = 4000;
        private int _MaxGroundingUrls = 5;
        private int _TimeoutSeconds = 300;
        private int _CoverageMaxQuestions = 12;

        #endregion
    }
}
