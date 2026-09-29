import { useTranslation } from 'react-i18next';
import CrawlPill from './CrawlPill';

// The steps of a crawl plan connectivity test, in order, with what was checked and what to fix.
function ConnectivityResultView({ result }) {
  const { t } = useTranslation();
  if (!result) return null;
  const layers = Array.isArray(result.layers) ? result.layers : [];
  return (
    <div className="crawl-connectivity">
      <div style={{ marginBottom: 8 }}>
        <CrawlPill label={result.success ? t('crawl.testPassed') : t('crawl.testFailed')} tone={result.success ? 'success' : 'danger'} />
      </div>
      <table className="data-table">
        <thead>
          <tr><th>{t('crawl.testStep')}</th><th>{t('crawl.testResult')}</th><th>{t('crawl.testDetail')}</th></tr>
        </thead>
        <tbody>
          {layers.map((l, i) => (
            <tr key={i}>
              <td>{l.name}</td>
              <td><CrawlPill label={l.success ? t('crawl.pass') : t('crawl.fail')} tone={l.success ? 'success' : 'danger'} /></td>
              <td>{l.message}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default ConnectivityResultView;
