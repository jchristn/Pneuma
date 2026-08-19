namespace Pneuma.Server.Services
{
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Storage;

    /// <summary>
    /// Bundle of the external integration clients, built once and shared by the server routes and the
    /// ingestion worker. Properties are typed to provider-neutral role interfaces so orchestration
    /// depends on capabilities (atomize, process, index, graph) rather than concrete backends.
    /// </summary>
    public class IntegrationClients
    {
        /// <summary>Document atomizer (type detection + cell extraction).</summary>
        public IAtomizer DocumentAtom { get; set; } = null!;

        /// <summary>Semantic processor with Partio's endpoint-administration surface.</summary>
        public IPartioClient Partio { get; set; } = null!;

        /// <summary>Full-text (lexical) search over the retrieval store (RecallDB).</summary>
        public IInvertedIndex Search { get; set; } = null!;

        /// <summary>Knowledge-graph repository.</summary>
        public IGraphRepository Graph { get; set; } = null!;

        /// <summary>Vector store (semantic retrieval + chunk storage), backed by RecallDB.</summary>
        public IVectorRepository Vectors { get; set; } = null!;

        /// <summary>Vector collection administration (RecallDB).</summary>
        public ICollectionStore Collections { get; set; } = null!;

        /// <summary>Blob store.</summary>
        public IBlobStore Blobs { get; set; } = null!;
    }
}
