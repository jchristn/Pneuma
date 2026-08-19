namespace Pneuma.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single versioned, ordered set of DDL statements applied idempotently and tracked in
    /// the schema_migrations table.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>Monotonic version number.</summary>
        public int Version { get; set; } = 0;

        /// <summary>Human-readable description.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Ordered DDL statements to execute for this version.</summary>
        public List<string> Statements { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a migration.</summary>
        public SchemaMigration()
        {
        }

        /// <summary>Instantiate a migration.</summary>
        /// <param name="version">Version number.</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">DDL statements.</param>
        public SchemaMigration(int version, string description, List<string> statements)
        {
            Version = version;
            Description = description ?? String.Empty;
            Statements = statements ?? new List<string>();
        }

        #endregion
    }
}
