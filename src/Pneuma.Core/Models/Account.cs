namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// Top-level organization that may own one or more tenants.
    /// </summary>
    public class Account
    {
        #region Public-Members

        /// <summary>Account identifier (prefix "acc_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Human-readable account name.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value; }
        }

        /// <summary>Whether the account is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the account is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateAccountId();
        private string _Name = String.Empty;

        #endregion
    }
}
