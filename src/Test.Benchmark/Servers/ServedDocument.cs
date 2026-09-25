namespace Test.Benchmark.Servers
{
    using System;

    /// <summary>
    /// A document as the corpus server serves it.
    /// </summary>
    public class ServedDocument
    {
        #region Public-Members

        /// <summary>
        /// HTTP content type.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Served text.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="contentType">HTTP content type.</param>
        /// <param name="text">Served text.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ServedDocument(string contentType, string text)
        {
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        #endregion
    }
}
