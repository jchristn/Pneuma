namespace Test.Benchmark.Client
{
    /// <summary>
    /// A typed API response with its HTTP status and client-measured latency.
    /// </summary>
    /// <typeparam name="T">Payload type.</typeparam>
    public class TimedResponse<T>
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code (0 when the request failed before a response).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Client-measured latency.
        /// </summary>
        public double ElapsedMs { get; set; } = 0.0;

        /// <summary>
        /// Parsed payload, when the call succeeded.
        /// </summary>
        public T? Value { get; set; } = default(T);

        /// <summary>
        /// Error text, when it failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// True for a 2xx response with a payload.
        /// </summary>
        public bool IsSuccess
        {
            get
            {
                return StatusCode >= 200 && StatusCode < 300 && Value != null;
            }
        }

        #endregion
    }
}
