namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Background worker that performs tenant cascade deletions asynchronously. A tenant the operator asks to
    /// delete is marked <see cref="TenantDeletionStatusEnum.Pending"/>; this worker claims it, marks it
    /// <see cref="TenantDeletionStatusEnum.Deleting"/>, runs the full cascade (every subject with its
    /// subordinate objects, the tenant's retrieval collections and LiteGraph tenant/graph, all tenant-scoped
    /// database rows, and the tenant row), and — on failure — marks it
    /// <see cref="TenantDeletionStatusEnum.Failed"/>. A tenant still marked Deleting at startup (an interrupted
    /// deletion) is resumed. Mirrors <see cref="SubjectDeletionWorker"/> and <see cref="LinkDeletionWorker"/>.
    /// </summary>
    public class TenantDeletionWorker
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CascadeDeletionService _Cascade;
        private readonly LoggingModule _Logging;
        private readonly int _PollIntervalMs;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tenant deletion worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cascade">Cascade deletion service.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="pollIntervalMs">How often to scan for pending deletions, in milliseconds (min 500).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public TenantDeletionWorker(DatabaseDriverBase db, CascadeDeletionService cascade, LoggingModule logging, int pollIntervalMs = 5000)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cascade = cascade ?? throw new ArgumentNullException(nameof(cascade));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _PollIntervalMs = pollIntervalMs < 500 ? 500 : pollIntervalMs;
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the background deletion loop.</summary>
        /// <param name="token">Shutdown token.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => RunAsync(token), token);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    List<Tenant> pending = await _Db.Tenants.EnumeratePendingDeletionAsync(token).ConfigureAwait(false);
                    foreach (Tenant tenant in pending)
                    {
                        if (token.IsCancellationRequested) break;
                        await DeleteOneAsync(tenant, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[TenantDeletionWorker] poll error: " + e.Message);
                }

                try
                {
                    await Task.Delay(_PollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task DeleteOneAsync(Tenant tenant, CancellationToken token)
        {
            try
            {
                if (tenant.DeletionStatus != TenantDeletionStatusEnum.Deleting)
                {
                    tenant.DeletionStatus = TenantDeletionStatusEnum.Deleting;
                    await _Db.Tenants.UpdateAsync(tenant, token).ConfigureAwait(false);
                }

                _Logging.Info("[TenantDeletionWorker] deleting tenant " + tenant.Id + " (" + tenant.Name + ").");
                await _Cascade.DeleteTenantCascadeAsync(tenant.Id, token).ConfigureAwait(false);
                _Logging.Info("[TenantDeletionWorker] deleted tenant " + tenant.Id + ".");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Leave it Deleting so it resumes on the next startup.
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[TenantDeletionWorker] failed to delete tenant " + tenant.Id + ": " + e.Message);
                try
                {
                    Tenant? current = await _Db.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false);
                    if (current != null)
                    {
                        current.DeletionStatus = TenantDeletionStatusEnum.Failed;
                        await _Db.Tenants.UpdateAsync(current, token).ConfigureAwait(false);
                    }
                }
                catch (Exception inner)
                {
                    _Logging.Warn("[TenantDeletionWorker] could not mark tenant " + tenant.Id + " Failed: " + inner.Message);
                }
            }
        }

        #endregion
    }
}
