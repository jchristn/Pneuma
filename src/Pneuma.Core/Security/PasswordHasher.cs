namespace Pneuma.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Computes and verifies SHA-256 password hashes (hex encoded, lowercase).
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>
        /// Compute the lowercase hex SHA-256 hash of a plaintext value.
        /// </summary>
        /// <param name="plaintext">Plaintext value.</param>
        /// <returns>64-character lowercase hex hash.</returns>
        /// <exception cref="ArgumentNullException">Thrown when plaintext is null.</exception>
        public static string Hash(string plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Verify a plaintext value against a stored hash using a constant-time comparison.
        /// </summary>
        /// <param name="plaintext">Candidate plaintext.</param>
        /// <param name="storedHash">Stored lowercase hex hash.</param>
        /// <returns>True when the hashes match.</returns>
        public static bool Verify(string plaintext, string storedHash)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (String.IsNullOrEmpty(storedHash)) return false;

            string computed = Hash(plaintext);
            byte[] a = Encoding.ASCII.GetBytes(computed);
            byte[] b = Encoding.ASCII.GetBytes(storedHash);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
