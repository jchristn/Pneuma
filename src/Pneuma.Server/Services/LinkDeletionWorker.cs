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
    /// Background worker that performs content-link cascade deletions asynchronously. A link the operator asks to
    /// delete is marked <see cref="LinkDeletionStatusEnum.Pending"/>; this worker claims it, marks it
    /// <see cref="LinkDeletionStatusEnum.Deleting"/>, runs the full cascade (ingestion jobs, job events, graph
    /// nodes/edges, index documents, blobs, S3 artifacts, and the link row), and — on failure — marks it
    /// <see cref="LinkDeletionStatusEnum.Failed"/>. A link still marked Deleting at startup (an interrupted
    /// deletion) is resumed. Mirrors <see cref="SubjectDeletionWorker"/>.
    /// </summary>
    public class LinkDeletionWorker
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CascadeDeletionService _Cascade;
        private readonly LoggingModule _Logging;
        private readonly int _PollIntervalMs;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the link deletion worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cascade">Cascade deletion service.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="pollIntervalMs">How often to scan for pending deletions, in milliseconds (min 500).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public LinkDeletionWorker(DatabaseDriverBase db, CascadeDeletionService cascade, LoggingModule logging, int pollIntervalMs = 5000)
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
                    List<SubjectLink> pending = await _Db.SubjectLinks.EnumeratePendingDeletionAsync(token).ConfigureAwait(false);
                    foreach (SubjectLink link in pending)
                    {
                        if (token.IsCancellationRequested) break;
                        await DeleteOneAsync(link, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[LinkDeletionWorker] poll error: " + e.Message);
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

        private async Task DeleteOneAsync(SubjectLink link, CancellationToken token)
        {
            try
            {
                if (link.DeletionStatus != LinkDeletionStatusEnum.Deleting)
                {
                    link.DeletionStatus = LinkDeletionStatusEnum.Deleting;
                    await _Db.SubjectLinks.UpdateAsync(link, token).ConfigureAwait(false);
                }

                _Logging.Info("[LinkDeletionWorker] deleting link " + link.Id + " (" + link.Url + ").");
                await _Cascade.DeleteLinkCascadeAsync(link.TenantId, link.Id, token).ConfigureAwait(false);
                _Logging.Info("[LinkDeletionWorker] deleted link " + link.Id + ".");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Leave it Deleting so it resumes on the next startup.
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[LinkDeletionWorker] failed to delete link " + link.Id + ": " + e.Message);
                try
                {
                    SubjectLink? current = await _Db.SubjectLinks.ReadAsync(link.TenantId, link.Id, token).ConfigureAwait(false);
                    if (current != null)
                    {
                        current.DeletionStatus = LinkDeletionStatusEnum.Failed;
                        await _Db.SubjectLinks.UpdateAsync(current, token).ConfigureAwait(false);
                    }
                }
                catch (Exception inner)
                {
                    _Logging.Warn("[LinkDeletionWorker] could not mark link " + link.Id + " Failed: " + inner.Message);
                }
            }
        }

        #endregion
    }
}
