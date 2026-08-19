namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Startup diagnostics settings: whether to probe external services at boot and whether an
    /// unreachable required dependency should abort startup.
    /// </summary>
    public class DiagnosticsSettings
    {
        #region Public-Members

        /// <summary>When true, external-service connectivity is probed at startup. Default true.</summary>
        public bool RunStartupProbes { get; set; } = true;

        /// <summary>
        /// When true, an unreachable external service aborts startup instead of logging a warning and
        /// continuing. Default false.
        /// </summary>
        public bool FailFastOnStartupProbe { get; set; } = false;

        #endregion
    }
}
