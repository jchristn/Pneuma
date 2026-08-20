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
    /// Background worker that performs subject cascade deletions asynchronously. A subject the operator asks to
    /// delete is marked <see cref="SubjectDeletionStatusEnum.Pending"/>; this worker claims it, marks it
    /// <see cref="SubjectDeletionStatusEnum.Deleting"/>, runs the full cascade (links, jobs, events, artifacts,
    /// graph, index, history, feedback, and the subject row), and — on failure — marks it
    /// <see cref="SubjectDeletionStatusEnum.Failed"/>. A subject still marked Deleting at startup (an interrupted
    /// deletion) is resumed.
    /// </summary>
    public class SubjectDeletionWorker
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CascadeDeletionService _Cascade;
        private readonly LoggingModule _Logging;
        private readonly int _PollIntervalMs;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the subject deletion worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cascade">Cascade deletion service.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="pollIntervalMs">How often to scan for pending deletions, in milliseconds (min 500).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public SubjectDeletionWorker(DatabaseDriverBase db, CascadeDeletionService cascade, LoggingModule logging, int pollIntervalMs = 5000)
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
                    List<Subject> pending = await _Db.Subjects.EnumeratePendingDeletionAsync(token).ConfigureAwait(false);
                    foreach (Subject subject in pending)
                    {
                        if (token.IsCancellationRequested) break;
                        await DeleteOneAsync(subject, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[SubjectDeletionWorker] poll error: " + e.Message);
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

        private async Task DeleteOneAsync(Subject subject, CancellationToken token)
        {
            try
            {
                if (subject.DeletionStatus != SubjectDeletionStatusEnum.Deleting)
                {
                    subject.DeletionStatus = SubjectDeletionStatusEnum.Deleting;
                    await _Db.Subjects.UpdateAsync(subject, token).ConfigureAwait(false);
                }

                _Logging.Info("[SubjectDeletionWorker] deleting subject " + subject.Id + " (" + subject.DisplayName + ").");
                await _Cascade.DeleteSubjectCascadeAsync(subject.TenantId, subject.Id, token).ConfigureAwait(false);
                _Logging.Info("[SubjectDeletionWorker] deleted subject " + subject.Id + ".");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Leave it Deleting so it resumes on the next startup.
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[SubjectDeletionWorker] failed to delete subject " + subject.Id + ": " + e.Message);
                try
                {
                    Subject? current = await _Db.Subjects.ReadAsync(subject.TenantId, subject.Id, token).ConfigureAwait(false);
                    if (current != null)
                    {
                        current.DeletionStatus = SubjectDeletionStatusEnum.Failed;
                        await _Db.Subjects.UpdateAsync(current, token).ConfigureAwait(false);
                    }
                }
                catch (Exception inner)
                {
                    _Logging.Warn("[SubjectDeletionWorker] could not mark subject " + subject.Id + " Failed: " + inner.Message);
                }
            }
        }

        #endregion
    }
}
