namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// Reads the wizard's reference web pages through the fetch-safety policy and turns them into one plain-text excerpt,
    /// each page introduced by its URL. A page that cannot be read is reported as a warning; only when none can be read
    /// does the brief step fail.
    /// </summary>
    public class WizardGroundingReader
    {
        #region Private-Members

        private readonly CrawlHttpClient? _Http;
        private readonly WizardSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="http">HTTP client with the fetch-safety policy; null disables reference URLs.</param>
        /// <param name="settings">Wizard limits.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public WizardGroundingReader(CrawlHttpClient? http, WizardSettings settings)
        {
            _Http = http;
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>The draft's reference URLs: the list plus the single legacy field, trimmed and without duplicates.</summary>
        /// <param name="draft">The draft.</param>
        /// <returns>The URLs, in order.</returns>
        public static List<string> UrlsOf(SubjectWizardDraft draft)
        {
            List<string> urls = new List<string>();
            if (draft == null) return urls;
            IEnumerable<string?> all = (draft.GroundingUrls ?? new List<string>()).Cast<string?>().Concat(new[] { draft.GroundingUrl });
            foreach (string? url in all)
            {
                if (String.IsNullOrWhiteSpace(url)) continue;
                string trimmed = url.Trim();
                if (!urls.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) urls.Add(trimmed);
            }
            return urls;
        }

        /// <summary>Read each URL and combine the text, sharing the grounding character budget between the pages.</summary>
        /// <param name="urls">The URLs.</param>
        /// <param name="warnings">Receives a line for each page that could not be read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The combined excerpt, or null when no page could be read (the warnings say why).</returns>
        public async Task<string?> ReadAsync(List<string> urls, List<string> warnings, CancellationToken token)
        {
            if (urls == null) throw new ArgumentNullException(nameof(urls));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            if (urls.Count == 0) return null;
            if (_Http == null)
            {
                warnings.Add("Reading reference URLs is not available on this server.");
                return null;
            }

            int budget = Math.Max(500, _Settings.MaxGroundingCharacters / urls.Count);
            StringBuilder sb = new StringBuilder();
            foreach (string url in urls)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    CrawlHttpResponse response = await _Http.GetAsync(url, null, token).ConfigureAwait(false);
                    string? text = HtmlTextExtractor.Extract(response.Bytes, response.ContentType, budget);
                    if (text == null)
                    {
                        warnings.Add(url + " was skipped: only web pages and text can be read (it returned " + (response.ContentType ?? "an unknown type") + ").");
                        continue;
                    }
                    if (String.IsNullOrWhiteSpace(text))
                    {
                        warnings.Add(url + " had no readable text.");
                        continue;
                    }
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append("Source: ").Append(url).Append('\n').Append(text);
                }
                catch (FetchBlockedException e)
                {
                    warnings.Add(url + " was refused: " + e.Message);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    warnings.Add(url + " could not be read: " + e.Message);
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        #endregion
    }
}
