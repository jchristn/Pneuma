namespace Pneuma.Server
{
    using System;

    /// <summary>
    /// Application entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            // Build-time hook: "install-browsers" downloads the Playwright Chromium build (and, with
            // --with-deps, its OS dependencies) so the ingestion crawler can render pages. Invoked
            // from the Docker image build; not part of normal server startup.
            if (args.Length > 0 && String.Equals(args[0], "install-browsers", StringComparison.OrdinalIgnoreCase))
            {
                return Microsoft.Playwright.Program.Main(new string[] { "install", "chromium", "--with-deps" });
            }

            Bootstrapper.Run(args);
            return 0;
        }
    }
}
