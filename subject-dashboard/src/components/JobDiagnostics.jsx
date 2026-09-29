import { useTranslation } from 'react-i18next';
import { formatDateTime, formatNumber } from '../utils/format';
import { stageLabel } from '../utils/ingestionActivity';
import './JobDiagnostics.css';

// Completeness counters shown for a job, in pipeline order. Each renders "produced / expected" where it has an
// expected value, so a gap (a failed batch, a missing vector) stands out.
const COUNTERS = [
  { key: 'cellsExtracted' },
  { key: 'classificationBatches', failedKey: 'classificationBatchesFailed' },
  { key: 'cellNodesCreated', failedKey: 'cellNodesFailed' },
  { key: 'summariesAttempted', failedKey: 'summariesFailed' },
  { key: 'chunksProduced' },
  { key: 'chunksEmbedded' },
  { key: 'chunksIndexed' }
];

// A job's failure category (with the fix to try), the warnings for work it dropped, its completeness counters,
// and its attempt history. Renders nothing for a job with none of these (a queued job, or an older record).
// A small pill with an explicit tone (the shared StatusPill maps domain statuses; these labels are not statuses).
function Pill({ label, tone }) {
  return <span className={`status-pill pill-${tone}`}>{label}</span>;
}

function JobDiagnostics({ job, attempts }) {
  const { t } = useTranslation();
  if (!job) return null;
  const warnings = Array.isArray(job.warnings) ? job.warnings : [];
  const completeness = job.completeness || null;
  const hasCounts = completeness && COUNTERS.some((c) => Number(completeness[c.key]) > 0);
  const attemptList = Array.isArray(attempts) ? attempts : [];

  if (!job.failureCategory && warnings.length === 0 && !hasCounts && attemptList.length === 0) return null;

  return (
    <div className="jdiag">
      {job.failureCategory && (
        <div className="jdiag-failure" role="note">
          <Pill label={t(`failureCategories.${job.failureCategory}.label`, job.failureCategory)} tone="danger" />
          <span className="jdiag-remediation">{t(`failureCategories.${job.failureCategory}.remediation`, '')}</span>
        </div>
      )}

      {warnings.length > 0 && (
        <div className="jdiag-section">
          <div className="jdiag-heading">{t('jobDiagnostics.warnings', { count: warnings.length })}</div>
          <ul className="jdiag-warnings">
            {warnings.map((w, i) => <li key={i}>{w}</li>)}
          </ul>
        </div>
      )}

      {hasCounts && (
        <div className="jdiag-section">
          <div className="jdiag-heading has-tip" title={t('jobDiagnostics.completenessTip')}>{t('jobDiagnostics.completeness')}</div>
          <dl className="jdiag-counts">
            {COUNTERS.map((c) => {
              const value = Number(completeness[c.key]) || 0;
              const failed = c.failedKey ? Number(completeness[c.failedKey]) || 0 : 0;
              return (
                <div key={c.key} className={failed > 0 ? 'jdiag-count is-lossy' : 'jdiag-count'}>
                  <dt>{t(`jobDiagnostics.counters.${c.key}`)}</dt>
                  <dd>
                    {formatNumber(value)}
                    {failed > 0 && <span className="jdiag-failed">{t('jobDiagnostics.failedCount', { count: failed })}</span>}
                  </dd>
                </div>
              );
            })}
          </dl>
        </div>
      )}

      {attemptList.length > 0 && (
        <div className="jdiag-section">
          <div className="jdiag-heading">{t('jobDiagnostics.attempts', { count: attemptList.length })}</div>
          <table className="jdiag-attempts">
            <thead>
              <tr>
                <th scope="col">{t('jobDiagnostics.attemptNumber')}</th>
                <th scope="col">{t('jobDiagnostics.attemptOutcome')}</th>
                <th scope="col">{t('jobDiagnostics.attemptStage')}</th>
                <th scope="col">{t('jobDiagnostics.attemptEnded')}</th>
                <th scope="col">{t('jobDiagnostics.attemptMessage')}</th>
              </tr>
            </thead>
            <tbody>
              {attemptList.map((a) => (
                <tr key={a.id || a.attemptNumber}>
                  <td>{formatNumber(a.attemptNumber)}</td>
                  <td>
                    {a.succeeded
                      ? <Pill label={t('jobDiagnostics.succeeded')} tone="success" />
                      : <Pill label={t(`failureCategories.${a.failureCategory}.label`, a.failureCategory || t('jobDiagnostics.failed'))} tone="danger" />}
                  </td>
                  <td>{stageLabel(a.stage)}</td>
                  <td>{formatDateTime(a.endedUtc)}</td>
                  <td className="jdiag-message">{a.message || ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default JobDiagnostics;
