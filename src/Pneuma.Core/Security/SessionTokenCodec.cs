namespace Pneuma.Core.Security
{
    using System;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Encodes and decodes opaque session tokens by AES-encrypting a JSON <see cref="TokenPayload"/>.
    /// </summary>
    public class SessionTokenCodec
    {
        #region Private-Members

        private readonly Aes256Cipher _Cipher;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the codec with a signing key.
        /// </summary>
        /// <param name="signingKey">Signing key material.</param>
        public SessionTokenCodec(string signingKey)
        {
            if (String.IsNullOrEmpty(signingKey)) throw new ArgumentNullException(nameof(signingKey));
            _Cipher = new Aes256Cipher(signingKey);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode a payload into an opaque token string.
        /// </summary>
        /// <param name="payload">Payload.</param>
        /// <returns>Opaque token.</returns>
        public string Encode(TokenPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            string json = Json.Serialize(payload);
            return _Cipher.Encrypt(json);
        }

        /// <summary>
        /// Decode an opaque token string into a payload, or return null when the token is invalid.
        /// </summary>
        /// <param name="token">Opaque token.</param>
        /// <returns>Payload, or null.</returns>
        public TokenPayload? Decode(string token)
        {
            if (String.IsNullOrEmpty(token)) return null;
            try
            {
                string json = _Cipher.Decrypt(token);
                return Json.Deserialize<TokenPayload>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
