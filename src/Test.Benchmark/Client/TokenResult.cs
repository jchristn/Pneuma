namespace Test.Benchmark.Client
{
    /// <summary>
    /// <c>POST /v1.0/token</c> response.
    /// </summary>
    public class TokenResult
    {
        #region Public-Members

        /// <summary>
        /// Session token.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Tenant the session is bound to.
        /// </summary>
        public string? TenantId { get; set; } = null;

        #endregion
    }
}
