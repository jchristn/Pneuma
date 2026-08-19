namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// User data access methods. Users are tenant-scoped; email is unique within a tenant.
    /// </summary>
    public interface IUserMethods
    {
        /// <summary>Create a user.</summary>
        /// <param name="user">User to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created user.</returns>
        Task<User> CreateAsync(User user, CancellationToken token = default);

        /// <summary>Read a user by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User, or null if not found.</returns>
        Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Read a user by identifier without tenant scoping.</summary>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User, or null if not found.</returns>
        Task<User?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>Read a user by email within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User, or null if not found.</returns>
        Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default);

        /// <summary>Enumerate users within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Users.</returns>
        Task<List<User>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Update a user.</summary>
        /// <param name="user">User to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated user.</returns>
        Task<User> UpdateAsync(User user, CancellationToken token = default);

        /// <summary>Delete a user by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
