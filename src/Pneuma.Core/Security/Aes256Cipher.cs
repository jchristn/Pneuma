namespace Pneuma.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// AES-256-CBC encryption with a random IV per operation. The IV is prepended to the ciphertext
    /// and the whole payload is Base64 encoded. Used for opaque session tokens and secret material.
    /// </summary>
    public class Aes256Cipher
    {
        #region Private-Members

        private readonly byte[] _Key;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the cipher with a signing key. The key string is hashed to 256 bits.
        /// </summary>
        /// <param name="keyMaterial">Key material (any length).</param>
        /// <exception cref="ArgumentNullException">Thrown when keyMaterial is null or empty.</exception>
        public Aes256Cipher(string keyMaterial)
        {
            if (String.IsNullOrEmpty(keyMaterial)) throw new ArgumentNullException(nameof(keyMaterial));
            _Key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encrypt plaintext and return a Base64 string of IV + ciphertext.
        /// </summary>
        /// <param name="plaintext">Plaintext.</param>
        /// <returns>Base64-encoded IV and ciphertext.</returns>
        /// <exception cref="ArgumentNullException">Thrown when plaintext is null.</exception>
        public string Encrypt(string plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            using (Aes aes = Aes.Create())
            {
                aes.Key = _Key;
                aes.GenerateIV();
                byte[] iv = aes.IV;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                    byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                    byte[] combined = new byte[iv.Length + cipherBytes.Length];
                    Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
                    Buffer.BlockCopy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);
                    return Base64UrlEncode(combined);
                }
            }
        }

        /// <summary>
        /// Decrypt a Base64 string of IV + ciphertext produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="payload">Base64-encoded IV and ciphertext.</param>
        /// <returns>Plaintext.</returns>
        /// <exception cref="ArgumentNullException">Thrown when payload is null or empty.</exception>
        /// <exception cref="FormatException">Thrown when the payload is malformed.</exception>
        public string Decrypt(string payload)
        {
            if (String.IsNullOrEmpty(payload)) throw new ArgumentNullException(nameof(payload));

            byte[] combined = Base64UrlDecode(payload);
            if (combined.Length < 16) throw new FormatException("Ciphertext payload is too short to contain an IV.");

            using (Aes aes = Aes.Create())
            {
                aes.Key = _Key;
                byte[] iv = new byte[16];
                Buffer.BlockCopy(combined, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] cipherBytes = new byte[combined.Length - iv.Length];
                    Buffer.BlockCopy(combined, iv.Length, cipherBytes, 0, cipherBytes.Length);
                    byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                default: break;
            }
            return Convert.FromBase64String(s);
        }

        #endregion
    }
}
