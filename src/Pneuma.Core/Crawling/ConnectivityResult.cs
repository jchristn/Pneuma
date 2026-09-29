namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The result of testing a crawl plan's connection: each step in order, stopping at the first failure, so the
    /// operator sees exactly which layer to fix.
    /// </summary>
    public class ConnectivityResult
    {
        #region Public-Members

        /// <summary>True when every step passed.</summary>
        public bool Success => Layers.Count > 0 && Layers.All(l => l.Success);

        /// <summary>The steps, in order.</summary>
        public List<ConnectivityLayer> Layers { get; set; } = new List<ConnectivityLayer>();

        #endregion

        #region Public-Methods

        /// <summary>Record a step.</summary>
        /// <param name="name">Step name.</param>
        /// <param name="success">True when the step passed.</param>
        /// <param name="message">What was checked and, on failure, what to fix.</param>
        /// <returns>True when the step passed (so callers can stop at the first failure).</returns>
        public bool Add(string name, bool success, string message)
        {
            Layers.Add(new ConnectivityLayer { Name = name, Success = success, Message = message });
            return success;
        }

        #endregion
    }
}
