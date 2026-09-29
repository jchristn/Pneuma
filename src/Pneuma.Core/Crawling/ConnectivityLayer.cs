namespace Pneuma.Core.Crawling
{
    using System;

    /// <summary>One step of a connectivity test (settings, DNS, TCP, authentication, root access), in order.</summary>
    public class ConnectivityLayer
    {
        #region Public-Members

        /// <summary>The step's name (for example "dns", "tcp", "auth", "root").</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>True when the step passed.</summary>
        public bool Success { get; set; } = false;

        /// <summary>What was checked and, on failure, what to fix.</summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
