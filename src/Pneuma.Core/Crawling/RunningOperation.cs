namespace Pneuma.Core.Crawling
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>An operation this server is enumerating or dispatching: its claim and its cancellation.</summary>
    public class RunningOperation : IDisposable
    {
        #region Public-Members

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; }

        /// <summary>Plan identifier.</summary>
        public string PlanId { get; }

        /// <summary>Operation identifier.</summary>
        public string OperationId { get; }

        /// <summary>The token the plan was claimed with.</summary>
        public string ClaimToken { get; }

        /// <summary>Cancelled when the operation is stopped.</summary>
        public CancellationToken Token => _Cts.Token;

        /// <summary>The background task running the operation, or null before it is started.</summary>
        public Task? Task { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="operationId">Operation identifier.</param>
        /// <param name="claimToken">Claim token.</param>
        public RunningOperation(string tenantId, string planId, string operationId, string claimToken)
        {
            TenantId = tenantId ?? String.Empty;
            PlanId = planId ?? String.Empty;
            OperationId = operationId ?? String.Empty;
            ClaimToken = claimToken ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>Request cancellation. Safe after disposal.</summary>
        public void Cancel()
        {
            if (_Disposed) return;
            try { _Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        /// <summary>Release the cancellation source.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>Dispose resources.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Cts.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
