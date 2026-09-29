namespace Pneuma.Core.Crawling.Crawlers
{
    using System;

    /// <summary>
    /// Maps a loopback host to <c>host.docker.internal</c> when Pneuma runs in a container, so a share or bucket
    /// configured as <c>localhost</c> reaches the machine hosting the container instead of the container itself.
    /// </summary>
    public static class ContainerHost
    {
        #region Public-Methods

        /// <summary>The host to connect to.</summary>
        /// <param name="host">The configured host.</param>
        /// <returns>The configured host, or <c>host.docker.internal</c> for a loopback host inside a container.</returns>
        public static string Resolve(string? host)
        {
            return Resolve(host, IsRunningInContainer());
        }

        /// <summary>The host to connect to, given whether Pneuma runs in a container.</summary>
        /// <param name="host">The configured host.</param>
        /// <param name="inContainer">True when running in a container.</param>
        /// <returns>The host.</returns>
        public static string Resolve(string? host, bool inContainer)
        {
            string trimmed = (host ?? String.Empty).Trim();
            if (!inContainer) return trimmed;
            if (String.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) || trimmed == "127.0.0.1" || trimmed == "::1" || trimmed == "[::1]")
                return "host.docker.internal";
            return trimmed;
        }

        /// <summary>True when the .NET container images' environment marker is set.</summary>
        /// <returns>True inside a container.</returns>
        public static bool IsRunningInContainer()
        {
            return String.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
