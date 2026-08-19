namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;

    /// <summary>
    /// Fans a tenant-creation event out to every registered <see cref="ITenantProvisioner"/> so subordinate
    /// services (RecallDB tenant + default collection today, extensible to others) are provisioned when a
    /// Pneuma tenant is created, seeded at first boot, or ensured at startup. Each provisioner runs
    /// best-effort: a failure is logged and never propagates, so an unavailable subordinate service does not
    /// block tenant creation. Provisioners are expected to be idempotent.
    /// </summary>
    public class TenantProvisioningService
    {
        #region Private-Members

        private readonly IReadOnlyList<ITenantProvisioner> _Provisioners;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[TenantProvisioning] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the provisioning service.</summary>
        /// <param name="provisioners">The subordinate-service provisioners to run on tenant creation.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public TenantProvisioningService(IReadOnlyList<ITenantProvisioner> provisioners, LoggingModule logging)
        {
            _Provisioners = provisioners ?? throw new ArgumentNullException(nameof(provisioners));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Provision every subordinate service for a tenant. Best-effort and idempotent; never throws.</summary>
        /// <param name="tenantId">The Pneuma tenant id.</param>
        /// <param name="tenantName">The tenant's display name.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task ProvisionAsync(string tenantId, string tenantName, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) return;

            foreach (ITenantProvisioner provisioner in _Provisioners)
            {
                try
                {
                    await provisioner.ProvisionAsync(tenantId, tenantName, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + provisioner.Name + " provisioning failed for tenant " + tenantId + " (continuing): " + e.Message);
                }
            }
        }

        #endregion
    }
}
