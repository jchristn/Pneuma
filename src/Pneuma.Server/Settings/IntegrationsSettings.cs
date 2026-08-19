namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Settings for the external services Pneuma orchestrates during ingestion and search.
    /// </summary>
    public class IntegrationsSettings
    {
        /// <summary>DocumentAtom settings.</summary>
        public DocumentAtomSettings DocumentAtom { get; set; } = new DocumentAtomSettings();

        /// <summary>Partio settings.</summary>
        public PartioSettings Partio { get; set; } = new PartioSettings();

        /// <summary>RecallDB settings (retrieval store: vector + full-text search).</summary>
        public RecallDbSettings RecallDb { get; set; } = new RecallDbSettings();

        /// <summary>LiteGraph settings.</summary>
        public LiteGraphSettings LiteGraph { get; set; } = new LiteGraphSettings();

        /// <summary>BLOB storage settings.</summary>
        public BlobSettings Blob { get; set; } = new BlobSettings();

        /// <summary>Resilience settings (timeout, concurrency, retry) applied to every integration client.</summary>
        public IntegrationResilienceSettings Resilience { get; set; } = new IntegrationResilienceSettings();
    }
}
