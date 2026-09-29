namespace Test.Shared.Support
{
    using System;

    /// <summary>The status code and body of a REST call made by <see cref="ApiClientHelper.CallAsync"/>.</summary>
    public class ApiResult
    {
        #region Public-Members

        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>Response body text; empty when there was none.</summary>
        public string Body { get; set; } = String.Empty;

        #endregion
    }
}
