namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;

    /// <summary>In-memory DocumentAtom fake. Returns a configurable detected type and fixed cells.</summary>
    public class FakeDocumentAtomClient : IDocumentAtomClient
    {
        private readonly string _Type;

        /// <summary>Instantiate with the type to report from detection ("Unknown" to force a failure).</summary>
        /// <param name="type">Detected type.</param>
        public FakeDocumentAtomClient(string type = "Text")
        {
            _Type = type;
        }

        /// <inheritdoc />
        public Task<TypeDetectResult> DetectTypeAsync(byte[] data, CancellationToken token = default)
        {
            return Task.FromResult(new TypeDetectResult { Type = _Type, MimeType = "text/plain", Extension = "txt" });
        }

        /// <inheritdoc />
        public Task<List<ExtractedCell>> ExtractCellsAsync(string documentType, byte[] data, CancellationToken token = default)
        {
            string text = Encoding.UTF8.GetString(data);
            List<ExtractedCell> cells = new List<ExtractedCell>
            {
                new ExtractedCell { Type = "Text", Text = text }
            };
            return Task.FromResult(cells);
        }
    }
}
