namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Microsoft.Playwright;

    /// <summary>
    /// Fetches source content by driving a headless Chromium browser (Playwright), so that
    /// JavaScript-rendered pages are captured as fully-rendered HTML. Non-HTML resources (PDFs,
    /// images, office documents, ...) are returned as their raw bytes. On any browser failure the
    /// fetch falls back to the supplied <see cref="IContentFetcher"/> (typically a plain HTTP GET).
    /// The browser is launched lazily and shared across fetches.
    /// </summary>
    public class PlaywrightContentFetcher : IContentFetcher, IAsyncDisposable
    {
        #region Private-Members

        private readonly IContentFetcher? _Fallback;
        private readonly int _NavigationTimeoutMs;
        private readonly string _UserAgent;
        private readonly SemaphoreSlim _InitLock = new SemaphoreSlim(1, 1);
        private IPlaywright? _Playwright;
        private IBrowser? _Browser;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a headless-browser content fetcher.</summary>
        /// <param name="fallback">Fetcher used when the browser is unavailable or navigation fails. May be null.</param>
        /// <param name="navigationTimeoutMs">Navigation timeout in milliseconds (minimum 1000).</param>
        /// <param name="userAgent">User-Agent header presented by the browser; falls back to <see cref="HttpContentFetcher.DefaultUserAgent"/> when null or empty.</param>
        public PlaywrightContentFetcher(IContentFetcher? fallback = null, int navigationTimeoutMs = 60000, string? userAgent = null)
        {
            _Fallback = fallback;
            _NavigationTimeoutMs = navigationTimeoutMs < 1000 ? 60000 : navigationTimeoutMs;
            _UserAgent = String.IsNullOrWhiteSpace(userAgent) ? HttpContentFetcher.DefaultUserAgent : userAgent!;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="url"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the browser fetch fails and no fallback is configured.</exception>
        public async Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));

            try
            {
                IBrowser browser = await GetBrowserAsync(token).ConfigureAwait(false);
                await using (IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = _UserAgent,
                    IgnoreHTTPSErrors = true,
                    // A real desktop viewport/locale so responsive sites render their full content rather
                    // than a minimal/mobile variant.
                    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                    Locale = "en-US"
                }).ConfigureAwait(false))
                {
                    IPage page = await context.NewPageAsync().ConfigureAwait(false);
                    IResponse? response = await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = _NavigationTimeoutMs
                    }).ConfigureAwait(false);

                    string contentType = String.Empty;
                    if (response != null && response.Headers != null && response.Headers.TryGetValue("content-type", out string? ct))
                    {
                        contentType = ct ?? String.Empty;
                    }

                    bool isHtml = String.IsNullOrEmpty(contentType)
                        || contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                        || contentType.Contains("application/xhtml", StringComparison.OrdinalIgnoreCase);

                    if (isHtml)
                    {
                        // Lazy-loaded and collapsed content is not present in the DOM at load time, so
                        // DocumentAtom would only see a fraction of the page. Auto-scroll to trigger
                        // lazy/infinite-scroll loading and force-open collapsible regions before capturing
                        // the fully-rendered HTML. All best-effort and time-boxed.
                        await RevealContentAsync(page, token).ConfigureAwait(false);

                        string html = await page.ContentAsync().ConfigureAwait(false);
                        return Encoding.UTF8.GetBytes(html);
                    }

                    if (response != null)
                    {
                        byte[] body = await response.BodyAsync().ConfigureAwait(false);
                        if (body != null && body.Length > 0) return body;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (_Fallback != null)
            {
                // Browser unavailable or navigation failed — fall through to the HTTP fallback below.
            }

            if (_Fallback != null) return await _Fallback.FetchAsync(url, token).ConfigureAwait(false);
            throw new InvalidOperationException("Headless-browser fetch failed and no fallback fetcher is configured for " + url + ".");
        }

        /// <summary>Dispose the shared browser and Playwright driver.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;

            if (_Browser != null)
            {
                try { await _Browser.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* best-effort teardown */ }
            }
            _Playwright?.Dispose();
            _InitLock.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task RevealContentAsync(IPage page, CancellationToken token)
        {
            try
            {
                // Incrementally scroll to the bottom (bounded) so lazy/infinite-scroll content loads, then
                // return to the top. Pure page-side JS with an internal step cap so it can never hang.
                await page.EvaluateAsync(
                    @"async () => {
                        const step = 1000, delayMs = 150, maxSteps = 40;
                        for (let i = 0; i < maxSteps; i++) {
                            window.scrollBy(0, step);
                            await new Promise(r => setTimeout(r, delayMs));
                            if ((window.innerHeight + window.scrollY) >= (document.body ? document.body.scrollHeight : 0)) break;
                        }
                        window.scrollTo(0, 0);
                    }").ConfigureAwait(false);

                // Give any content loaded by scrolling a chance to settle (best-effort, short bound).
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 }).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Network never fully idles on some pages — proceed with what we have.
                }

                // Force-open every <details> element so collapsed content is present in the captured HTML.
                await page.EvaluateAsync(
                    @"() => {
                        let changed = 0;
                        document.querySelectorAll('details').forEach((el) => { if (!el.open) { el.open = true; changed++; } });
                        return changed;
                    }").ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Revealing extra content is best-effort; fall back to whatever is already rendered.
            }
        }

        private async Task<IBrowser> GetBrowserAsync(CancellationToken token)
        {
            if (_Browser != null) return _Browser;
            await _InitLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Browser != null) return _Browser;
                _Playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                _Browser = await _Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    // Required for Chromium to run as root inside a container.
                    Args = new[] { "--no-sandbox", "--disable-dev-shm-usage" }
                }).ConfigureAwait(false);
                return _Browser;
            }
            finally
            {
                _InitLock.Release();
            }
        }

        #endregion
    }
}
