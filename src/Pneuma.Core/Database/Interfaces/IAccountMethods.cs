namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Account data access methods.
    /// </summary>
    public interface IAccountMethods
    {
        /// <summary>Create an account.</summary>
        /// <param name="account">Account to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created account.</returns>
        Task<Account> CreateAsync(Account account, CancellationToken token = default);

        /// <summary>Read an account by identifier.</summary>
        /// <param name="id">Account identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Account, or null if not found.</returns>
        Task<Account?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate all accounts.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Accounts.</returns>
        Task<List<Account>> EnumerateAsync(CancellationToken token = default);

        /// <summary>Update an account.</summary>
        /// <param name="account">Account to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated account.</returns>
        Task<Account> UpdateAsync(Account account, CancellationToken token = default);

        /// <summary>Delete an account by identifier.</summary>
        /// <param name="id">Account identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
