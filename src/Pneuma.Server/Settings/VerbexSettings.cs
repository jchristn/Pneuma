namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Verbex integration settings (inverted index and search).
    /// </summary>
    public class VerbexSettings
    {
        /// <summary>Base URL of the Verbex server.</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:8600/";

        /// <summary>Bearer token for Verbex (global admin token or tenant credential token).</summary>
        public string? BearerToken { get; set; } = "verbexadmin";

        /// <summary>Verbex tenant identifier used for index creation.</summary>
        public string TenantId { get; set; } = "default";

        /// <summary>Name of the Pneuma index within Verbex.</summary>
        public string IndexName { get; set; } = "pneuma";
    }
}
