namespace Pneuma.Server.Settings
{
    /// <summary>
    /// LiteGraph integration settings (knowledge graph store).
    /// </summary>
    public class LiteGraphSettings
    {
        /// <summary>Base URL of the LiteGraph server.</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:8701/";

        /// <summary>Bearer token for LiteGraph, if required.</summary>
        public string? BearerToken { get; set; } = null;

        /// <summary>LiteGraph tenant identifier, if required.</summary>
        public string? TenantGuid { get; set; } = null;

        /// <summary>LiteGraph graph identifier that hosts the Pneuma knowledge graph.</summary>
        public string? GraphGuid { get; set; } = null;
    }
}
