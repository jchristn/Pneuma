namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral document atomizer: detects a document's type from its raw bytes and extracts
    /// its content into semantic cells. Backed today by DocumentAtom.
    /// </summary>
    public interface IAtomizer
    {
        /// <summary>Detect the type of a document from its raw bytes.</summary>
        /// <param name="data">Raw document bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detection result.</returns>
        Task<TypeDetectResult> DetectTypeAsync(byte[] data, CancellationToken token = default);

        /// <summary>Extract semantic cells for a known document type.</summary>
        /// <param name="documentType">Detected document type.</param>
        /// <param name="data">Raw document bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The extracted cells.</returns>
        Task<List<ExtractedCell>> ExtractCellsAsync(string documentType, byte[] data, CancellationToken token = default);
    }
}
