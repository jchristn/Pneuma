namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>The outcome of an ontology service action: a value, or an HTTP-style status with an error.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    public class OntologyResult<T>
    {
        #region Public-Members

        /// <summary>HTTP status the action maps to (200, 201, 400, 404, or 409).</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>The error, when the status is 400 or above.</summary>
        public string? Error { get; set; } = null;

        /// <summary>Individual problems behind a 400 (validation messages), if any.</summary>
        public List<string> Problems { get; set; } = new List<string>();

        /// <summary>The value, when the action succeeded.</summary>
        public T? Value { get; set; } = default(T);

        /// <summary>Whether the action succeeded.</summary>
        public bool Succeeded { get { return StatusCode < 400; } }

        #endregion

        #region Public-Methods

        /// <summary>A successful result.</summary>
        /// <param name="value">The value.</param>
        /// <param name="statusCode">Status (200 or 201).</param>
        /// <returns>The result.</returns>
        public static OntologyResult<T> Ok(T value, int statusCode = 200)
        {
            return new OntologyResult<T> { StatusCode = statusCode, Value = value };
        }

        /// <summary>A failed result.</summary>
        /// <param name="statusCode">Status (400 or above).</param>
        /// <param name="error">What went wrong.</param>
        /// <param name="problems">Individual problems, if any.</param>
        /// <returns>The result.</returns>
        public static OntologyResult<T> Fail(int statusCode, string error, List<string>? problems = null)
        {
            return new OntologyResult<T> { StatusCode = statusCode, Error = error, Problems = problems ?? new List<string>() };
        }

        #endregion
    }
}
