namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Requests;

    /// <summary>
    /// Stores each embedded chunk as a RecallDB document (content + embedding + provenance tags) in the job's
    /// collection. Chunks live only in the retrieval store, not the graph; each document's <c>litegraphNodeId</c>
    /// tag points at the chunk's originating Cell node so a retrieval hit still resolves to a graph node. A job
    /// with no target collection is a deterministic hard failure.
    /// </summary>
    public class IndexingStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public IndexingStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Indexing;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IngestionHardFailException">Thrown when the job has no target collection assigned.</exception>
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            MergeResult merge = context.Merge;
            List<SemanticChunk> chunks = context.Chunks;

            if (String.IsNullOrEmpty(job.CollectionId)) throw new IngestionHardFailException(IngestionStageEnum.Indexing, "Ingestion job " + job.Id + " has no target collection assigned.");

            string sourceNodeId = merge.NodeIds.Count > 0 ? merge.NodeIds[0] : String.Empty;
            List<ChunkDocument> documents = new List<ChunkDocument>();
            int position = 0;

            foreach (SemanticChunk chunk in chunks)
            {
                if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                if (chunk.Embeddings == null || chunk.Embeddings.Count == 0) continue;

                // The chunk lives only in RecallDB. Its litegraphNodeId resolves to the Cell node it was derived
                // from (created during merge), falling back to the Source node when the cell has no node.
                string targetNodeId = String.IsNullOrEmpty(chunk.CellNodeId) ? sourceNodeId : chunk.CellNodeId!;

                // Provenance tags round-trip on search hits: litegraphNodeId resolves the hit to a graph node,
                // jobId scopes cascade deletion, and linkId/tenantId/subjectId scope search filters.
                Dictionary<string, string> tags = new Dictionary<string, string>
                {
                    { "litegraphNodeId", targetNodeId },
                    { "linkId", job.LinkId },
                    { "tenantId", job.TenantId },
                    { "subjectId", job.SubjectId },
                    { "jobId", job.Id },
                    { "sourceUrl", job.SourceUrl },
                    { "chunkKind", String.IsNullOrEmpty(chunk.Kind) ? "content" : chunk.Kind }
                };
                if (!String.IsNullOrEmpty(job.DocumentType))
                {
                    tags["documentType"] = job.DocumentType!;
                    // The source document type is also exposed as a filterable label so retrieval can scope by it
                    // (html, pdf, …) exactly like an operator-supplied label.
                    tags[RetrievalFilter.LabelTagKeyFor(job.DocumentType!)] = job.DocumentType!;
                }

                // Operator-supplied labels and tags from the originating link, stamped onto every chunk so
                // retrieval can be scoped to them. Reserved provenance keys above are never overwritten.
                IngestionMetadata.ApplyUserMetadata(tags, job);

                documents.Add(new ChunkDocument
                {
                    DocumentKey = job.Id + "_" + position.ToString(CultureInfo.InvariantCulture),
                    DocumentId = job.LinkId,
                    Position = position,
                    Content = chunk.Text,
                    Embedding = chunk.Embeddings,
                    Tags = tags
                });
                position++;
            }

            if (documents.Count > 0)
            {
                await _Deps.Vectors.StoreChunksAsync(job.TenantId, job.CollectionId, documents, token).ConfigureAwait(false);
            }

            context.Message = "Search indexing complete — stored " + documents.Count + " chunk document(s) in collection " + job.CollectionId + ", each linked back to its knowledge-graph node.";
        }

        #endregion
    }
}
