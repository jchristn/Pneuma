namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Administrator data access methods.
    /// </summary>
    public interface IAdministratorMethods
    {
        /// <summary>Create an administrator.</summary>
        /// <param name="administrator">Administrator to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created administrator.</returns>
        Task<Administrator> CreateAsync(Administrator administrator, CancellationToken token = default);

        /// <summary>Read an administrator by identifier.</summary>
        /// <param name="id">Administrator identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrator, or null if not found.</returns>
        Task<Administrator?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Read an administrator by email.</summary>
        /// <param name="email">Email address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrator, or null if not found.</returns>
        Task<Administrator?> ReadByEmailAsync(string email, CancellationToken token = default);

        /// <summary>Enumerate all administrators.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrators.</returns>
        Task<List<Administrator>> EnumerateAsync(CancellationToken token = default);

        /// <summary>Update an administrator.</summary>
        /// <param name="administrator">Administrator to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated administrator.</returns>
        Task<Administrator> UpdateAsync(Administrator administrator, CancellationToken token = default);

        /// <summary>Delete an administrator by identifier.</summary>
        /// <param name="id">Administrator identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
