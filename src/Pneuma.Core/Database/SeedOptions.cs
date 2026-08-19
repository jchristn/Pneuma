namespace Pneuma.Core.Database
{
    /// <summary>
    /// First-boot seeding options. Defaults are suitable for local development and tests.
    /// </summary>
    public class SeedOptions
    {
        #region Public-Members

        /// <summary>Default account name.</summary>
        public string AccountName { get; set; } = "Pneuma";

        /// <summary>Default system tenant name.</summary>
        public string TenantName { get; set; } = "System";

        /// <summary>Default administrator email (also the admin dashboard login).</summary>
        public string AdminEmail { get; set; } = "admin@pneuma";

        /// <summary>Default administrator password (plaintext; hashed during seeding).</summary>
        public string AdminPassword { get; set; } = "password";

        /// <summary>Default administrator first name.</summary>
        public string AdminFirstName { get; set; } = "Pneuma";

        /// <summary>Default administrator last name.</summary>
        public string AdminLastName { get; set; } = "Administrator";

        /// <summary>Base URL for the default Ollama runner.</summary>
        public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

        /// <summary>Base URL for the default OpenAI runner.</summary>
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

        /// <summary>Base URL for the default Gemini runner.</summary>
        public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

        #endregion
    }
}
