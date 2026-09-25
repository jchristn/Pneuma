namespace Test.Benchmark.Client
{
    /// <summary>
    /// One chat message.
    /// </summary>
    public class ChatMessageBody
    {
        #region Public-Members

        /// <summary>
        /// user, assistant, or system.
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// Message text.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        #endregion
    }
}
