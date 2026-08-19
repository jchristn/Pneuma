namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Authentication scheme that drove a request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthSchemeEnum
    {
        /// <summary>Bearer session token via Authorization header.</summary>
        BearerToken,
        /// <summary>Session token via x-token header.</summary>
        XToken,
        /// <summary>Email/password login headers.</summary>
        PasswordHeaders,
        /// <summary>Access key + secret key direct headers.</summary>
        AccessKeySecret,
        /// <summary>Access key + request signature.</summary>
        AccessKeySignature,
        /// <summary>System administrator API key.</summary>
        AdminApiKey
    }
}
