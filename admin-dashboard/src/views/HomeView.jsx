import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ActivityChart, { RANGES, rangeToParams } from '../components/ActivityChart';
import ErrorBanner from '../components/ErrorBanner';
import Icon from '../components/Icon';
import { formatNumber } from '../i18n/formatters';

// External system UIs, linked with their default credentials. Hosts use localhost since these
// links open in the operator's browser against the docker-compose published ports.
const SYSTEMS = [
  { name: 'Grafana', url: 'http://localhost:3000', cred: 'admin / admin' },
  { name: 'Prometheus', url: 'http://localhost:9090', cred: 'no auth' },
  { name: 'User Dashboard', url: 'http://localhost:3012', cred: 'admin@pneuma / password' },
  { name: 'Subject Dashboard', url: 'http://localhost:3011', cred: 'admin@pneuma / password' },
  { name: 'Partio', url: 'http://localhost:8401', cred: 'token: partioadmin' },
  { name: 'RecallDB', url: 'http://localhost:8601', cred: 'token: recalldbadmin' },
  { name: 'DocumentAtom', url: 'http://localhost:3002', cred: 'no auth' },
  { name: 'LiteGraph', url: 'http://localhost:3001', cred: 'token: litegraphadmin' },
  { name: 'Less3 (S3)', url: 'http://localhost:3003', cred: 'admin key: less3admin' }
];

function Kpi({ label, value, tone = '', onClick }) {
  return (
    <button type="button" className={`kpi-tile ${onClick ? 'clickable' : ''}`} onClick={onClick} style={!onClick ? { cursor: 'default' } : undefined}>
      <span className="kpi-label">{label}</span>
      <span className={`kpi-value ${tone}`}>{value === null ? '—' : formatNumber(value)}</span>
    </button>
  );
}

async function countOf(promise) {
  try {
    const resp = await promise;
    return normalizeList(resp).totalCount;
  } catch {
    return null;
  }
}

function HomeView() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { apiClient } = useAuth();

  const [counts, setCounts] = useState({ tenants: null, users: null, subjects: null, links: null, jobsQueued: null, jobsFailed: null });
  const [rangeId, setRangeId] = useState('day');
  const [summary, setSummary] = useState(null);
  const [chartLoading, setChartLoading] = useState(false);
  const [error, setError] = useState(null);

  const loadCounts = useCallback(async () => {
    const [tenants, users, subjects, links, jobsQueued, jobsFailed] = await Promise.all([
      countOf(apiClient.list('tenants', { maxResults: 1 })),
      countOf(apiClient.list('users', { maxResults: 1 })),
      countOf(apiClient.list('subjects', { maxResults: 1 })),
      countOf(apiClient.list('links', { maxResults: 1 })),
      countOf(apiClient.list('jobs', { status: 'Queued', maxResults: 1 })),
      countOf(apiClient.list('jobs', { status: 'Failed', maxResults: 1 }))
    ]);
    setCounts({ tenants, users, subjects, links, jobsQueued, jobsFailed });
  }, [apiClient]);

  const loadSummary = useCallback(async () => {
    setChartLoading(true);
    setError(null);
    try {
      const params = rangeToParams(rangeId);
      const resp = await apiClient.getRequestHistorySummary(params);
      setSummary(resp);
    } catch (err) {
      setError(err?.message || 'Failed to load request activity');
      setSummary(null);
    } finally {
      setChartLoading(false);
    }
  }, [apiClient, rangeId]);

  useEffect(() => { loadCounts(); }, [loadCounts]);
  useEffect(() => { loadSummary(); }, [loadSummary]);

  const totals = {
    total: summary?.totalCount ?? summary?.TotalCount ?? 0,
    success: summary?.totalSuccess ?? summary?.TotalSuccess ?? 0,
    failure: summary?.totalFailure ?? summary?.TotalFailure ?? 0
  };

  const handleBucketClick = (bucket) => {
    navigate(`/dashboard/requests?fromUtc=${encodeURIComponent(bucket.startUtc)}&toUtc=${encodeURIComponent(bucket.endUtc)}`);
  };

  return (
    <div>
      <PageHeader title={t('home.title')} subtitle={t('home.subtitle')} />

      <div className="kpi-grid">
        <Kpi label={t('home.kpiTenants')} value={counts.tenants} onClick={() => navigate('/dashboard/tenants')} />
        <Kpi label={t('home.kpiUsers')} value={counts.users} onClick={() => navigate('/dashboard/users')} />
        <Kpi label={t('home.kpiSubjects')} value={counts.subjects} onClick={() => navigate('/dashboard/subjects')} />
        <Kpi label={t('home.kpiLinks')} value={counts.links} onClick={() => navigate('/dashboard/links')} />
        <Kpi label={t('home.kpiJobsQueued')} value={counts.jobsQueued} onClick={() => navigate('/dashboard/jobs')} />
        <Kpi label={t('home.kpiJobsFailed')} value={counts.jobsFailed} tone="danger" onClick={() => navigate('/dashboard/jobs')} />
      </div>

      {error && <ErrorBanner message={error} onRetry={loadSummary} onDismiss={() => setError(null)} />}

      <div className="chart-card" style={{ marginBottom: 'var(--spacing-lg)' }}>
        <div className="chart-header">
          <h2>{t('home.activity')}</h2>
          <div className="chart-controls">
            <div className="segmented">
              {Object.keys(RANGES).map((id) => (
                <button key={id} type="button" className={rangeId === id ? 'active' : ''} onClick={() => setRangeId(id)}>
                  {t(RANGES[id].labelKey)}
                </button>
              ))}
            </div>
            <button type="button" className="icon-button" onClick={loadSummary} title={t('common.refresh')} aria-label={t('common.refresh')} disabled={chartLoading}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={chartLoading ? { animation: 'spin 1s linear infinite' } : undefined}>
                <polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" />
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
              </svg>
            </button>
          </div>
        </div>
        <div className="chart-stats">
          <div className="chart-stat"><span className="chart-stat-value">{formatNumber(totals.total)}</span><span className="chart-stat-label">{t('chart.total')}</span></div>
          <div className="chart-stat"><span className="chart-stat-value" style={{ color: 'var(--color-success)' }}>{formatNumber(totals.success)}</span><span className="chart-stat-label">{t('chart.success')}</span></div>
          <div className="chart-stat"><span className="chart-stat-value" style={{ color: 'var(--color-danger)' }}>{formatNumber(totals.failure)}</span><span className="chart-stat-label">{t('chart.failed')}</span></div>
        </div>
        <ActivityChart summary={summary} rangeId={rangeId} onBucketClick={handleBucketClick} />
      </div>

      <div className="section">
        <h2>{t('home.quickLinks')}</h2>
        <div className="quick-links">
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/subjects')}><Icon name="users" /> {t('nav.subjects')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/jobs')}><Icon name="queue" /> {t('nav.jobs')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/model-runners')}><Icon name="cpu" /> {t('nav.modelRunners')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/explorer')}><Icon name="play" /> {t('nav.explorer')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/requests')}><Icon name="chart" /> {t('nav.requests')}</button>
        </div>
      </div>

      <div className="section">
        <h2>{t('home.systems')}</h2>
        <div className="systems-grid">
          {SYSTEMS.map((sys) => (
            <a key={sys.name} className="system-card" href={sys.url} target="_blank" rel="noopener noreferrer">
              <span className="system-card-head">
                <span className="system-name">{sys.name}</span>
                <Icon name="link" />
              </span>
              <span className="system-url">{sys.url.replace('http://', '')}</span>
              <span className="system-cred">{sys.cred}</span>
            </a>
          ))}
        </div>
      </div>
    </div>
  );
}

export default HomeView;
