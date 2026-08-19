namespace Pneuma.Sdk.Responses
{
    /// <summary>
    /// Response reporting how many records a bulk operation affected.
    /// </summary>
    public class DeletedCountResponse
    {
        /// <summary>Number of records deleted.</summary>
        public int DeletedCount { get; set; } = 0;
    }
}
