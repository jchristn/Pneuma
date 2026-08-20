namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test message handler that records every request URI it sees and returns a single canned response.
    /// Used to assert the exact URL/query a real integration client builds (for example the LiteGraph
    /// include-data read flags) without a live downstream service.
    /// </summary>
    public sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        #region Private-Members

        private readonly string _ResponseBody;
        private readonly HttpStatusCode _StatusCode;
        private readonly List<string> _RequestUris = new List<string>();
        private readonly List<string> _RequestBodies = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>The request URIs (absolute) seen by the handler, in order.</summary>
        public IReadOnlyList<string> RequestUris { get { return _RequestUris; } }

        /// <summary>The most recent request URI, or null when none has been seen.</summary>
        public string? LastRequestUri { get { return _RequestUris.Count > 0 ? _RequestUris[_RequestUris.Count - 1] : null; } }

        /// <summary>The request bodies seen by the handler, in order (empty string when a request had no body).</summary>
        public IReadOnlyList<string> RequestBodies { get { return _RequestBodies; } }

        /// <summary>The most recent request body, or null when none has been seen.</summary>
        public string? LastRequestBody { get { return _RequestBodies.Count > 0 ? _RequestBodies[_RequestBodies.Count - 1] : null; } }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize the handler with the canned response it returns for every request.</summary>
        /// <param name="responseBody">Response body returned for each request.</param>
        /// <param name="statusCode">Status code returned for each request. Defaults to 200 OK.</param>
        public RecordingHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _ResponseBody = responseBody ?? String.Empty;
            _StatusCode = statusCode;
        }

        #endregion

        #region Protected-Methods

        /// <summary>Record the request URI and return the canned response.</summary>
        /// <param name="request">Request message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The canned response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null) _RequestUris.Add(request.RequestUri.AbsoluteUri);
            _RequestBodies.Add(request.Content != null ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult() : String.Empty);
            HttpResponseMessage response = new HttpResponseMessage(_StatusCode) { Content = new StringContent(_ResponseBody) };
            return Task.FromResult(response);
        }

        #endregion
    }
}
