namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// HTTP client for DocumentAtom type detection and semantic cell (atom) extraction.
    /// DocumentAtom requires no authentication and uses no version prefix.
    /// </summary>
    public class DocumentAtomClient : IntegrationClientBase, IDocumentAtomClient, IServiceProbe
    {
        #region Private-Members

        private readonly string _BaseUrl;

        private static readonly Dictionary<string, string> _RouteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pdf", "pdf" },
            { "Docx", "word" },
            { "Xlsx", "excel" },
            { "Pptx", "powerpoint" },
            { "Csv", "csv" },
            { "Tsv", "csv" },
            { "Json", "json" },
            { "Xml", "xml" },
            { "Html", "html" },
            { "Markdown", "markdown" },
            { "Text", "text" },
            { "Rtf", "rtf" },
            { "Png", "png" },
            { "Jpeg", "png" },
            { "Gif", "png" },
            { "Bmp", "png" },
            { "Tiff", "png" },
            { "WebP", "png" }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new DocumentAtom client.</summary>
        /// <param name="baseUrl">Base URL of the DocumentAtom service.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        public DocumentAtomClient(
            string baseUrl,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("documentatom", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default)
        {
            return ProbeResilientAsync("probe", () => new HttpRequestMessage(HttpMethod.Get, _BaseUrl + "/"), token);
        }

        /// <inheritdoc />
        public async Task<TypeDetectResult> DetectTypeAsync(byte[] data, CancellationToken token = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            string body = await SendResilientAsync(
                "/typedetect",
                () =>
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _BaseUrl + "/typedetect");
                    ByteArrayContent content = new ByteArrayContent(data);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    request.Content = content;
                    return request;
                },
                isWrite: false,
                token).ConfigureAwait(false);

            TypeDetectResult? result = Json.Deserialize<TypeDetectResult>(body);
            return result ?? new TypeDetectResult();
        }

        /// <inheritdoc />
        public async Task<List<ExtractedCell>> ExtractCellsAsync(string documentType, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(documentType)) throw new ArgumentNullException(nameof(documentType));
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (!_RouteMap.TryGetValue(documentType, out string? route) || String.IsNullOrEmpty(route))
            {
                throw new NotSupportedException("DocumentAtom cell extraction is not supported for document type '" + documentType + "'.");
            }

            string requestJson = Json.Serialize(new
            {
                Settings = (object?)null,
                Data = Convert.ToBase64String(data)
            });

            string body = await SendResilientAsync(
                RouteNormalizer.Normalize("/atom/" + route),
                () =>
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _BaseUrl + "/atom/" + route);
                    request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    return request;
                },
                isWrite: false,
                token).ConfigureAwait(false);

            List<AtomDto>? atoms = Json.Deserialize<List<AtomDto>>(body);
            List<ExtractedCell> cells = new List<ExtractedCell>();
            if (atoms == null) return cells;

            // DocumentAtom returns atoms as a hierarchy: header/container atoms carry their
            // content in nested "Quarks". Flatten the whole tree so every atom (paragraphs,
            // lists, tables, and nested content) becomes a cell — not just the top level.
            foreach (AtomDto atom in atoms)
            {
                FlattenAtom(atom, cells);
            }

            return cells;
        }

        #endregion

        #region Private-Methods

        private static void FlattenAtom(AtomDto? atom, List<ExtractedCell> cells)
        {
            if (atom == null) return;

            string text;
            if (!String.IsNullOrEmpty(atom.Text))
            {
                text = atom.Text;
            }
            else if (atom.UnorderedList != null && atom.UnorderedList.Count > 0)
            {
                text = String.Join("\n", atom.UnorderedList);
            }
            else if (atom.OrderedList != null && atom.OrderedList.Count > 0)
            {
                text = String.Join("\n", atom.OrderedList);
            }
            else
            {
                text = BuildTableText(atom.Table);
            }

            if (!String.IsNullOrWhiteSpace(text))
            {
                cells.Add(new ExtractedCell
                {
                    Type = atom.Type ?? "Text",
                    Title = atom.Title,
                    Text = text
                });
            }

            // Recurse into nested atoms (a header's child content, a container's block children, etc.).
            if (atom.Quarks != null)
            {
                foreach (AtomDto child in atom.Quarks)
                {
                    FlattenAtom(child, cells);
                }
            }
        }

        private static string BuildTableText(AtomTableDto? table)
        {
            if (table == null || table.Rows == null || table.Rows.Count == 0) return String.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (Dictionary<string, JsonElement> row in table.Rows)
            {
                if (row == null || row.Count == 0) continue;
                List<string> cellValues = new List<string>();
                foreach (KeyValuePair<string, JsonElement> cell in row)
                {
                    // Table cells are genuinely schemaless (arbitrary, dynamic columns), so the value is
                    // read as a JSON element and rendered to text rather than bound to a fixed contract.
                    string value = cell.Value.ValueKind == JsonValueKind.String
                        ? (cell.Value.GetString() ?? String.Empty)
                        : cell.Value.GetRawText();
                    if (!String.IsNullOrWhiteSpace(value)) cellValues.Add(value);
                }
                if (cellValues.Count > 0) sb.AppendLine(String.Join(" | ", cellValues));
            }
            return sb.ToString().Trim();
        }

        #endregion

        #region Private-Types

        private class AtomDto
        {
            public string? Type { get; set; }
            public string? Text { get; set; }
            public string? Title { get; set; }
            public List<string>? UnorderedList { get; set; }
            public List<string>? OrderedList { get; set; }
            public AtomTableDto? Table { get; set; }
            public List<AtomDto>? Quarks { get; set; }
        }

        private class AtomTableDto
        {
            // Rows are dictionaries of dynamic column name -> cell value; cell values are schemaless.
            public List<Dictionary<string, JsonElement>>? Rows { get; set; }
        }

        #endregion
    }
}
