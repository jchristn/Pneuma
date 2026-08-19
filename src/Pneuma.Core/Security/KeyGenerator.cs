namespace Pneuma.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Generates credential access keys and secret keys with high-entropy random material.
    /// </summary>
    public static class KeyGenerator
    {
        #region Private-Members

        private const string _Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int _AccessRandomLength = 32;
        private const int _SecretRandomLength = 48;

        #endregion

        #region Public-Methods

        /// <summary>Generate an access key ("access_" + 32 random alphanumerics).</summary>
        /// <returns>Access key.</returns>
        public static string GenerateAccessKey()
        {
            return "access_" + RandomString(_AccessRandomLength);
        }

        /// <summary>Generate a secret key ("secret_" + 48 random alphanumerics).</summary>
        /// <returns>Secret key.</returns>
        public static string GenerateSecretKey()
        {
            return "secret_" + RandomString(_SecretRandomLength);
        }

        #endregion

        #region Private-Methods

        private static string RandomString(int length)
        {
            StringBuilder sb = new StringBuilder(length);
            byte[] buffer = RandomNumberGenerator.GetBytes(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(_Alphabet[buffer[i] % _Alphabet.Length]);
            }
            return sb.ToString();
        }

        #endregion
    }
}
