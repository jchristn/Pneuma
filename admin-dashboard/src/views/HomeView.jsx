import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ActivityChart, { rangeToParams } from '../components/ActivityChart';
import ChartRangeControls from '../components/ChartRangeControls';
import IngestionActivityChart from '../components/IngestionActivityChart';
import ErrorBanner from '../components/ErrorBanner';
import Icon from '../components/Icon';
import { formatNumber } from '../i18n/formatters';
import { copyChartPng } from '../utils/chartExport';
import { normalizeIngestionBuckets, stagesPresent, stageColor, stageLabel } from '../utils/ingestionActivity';

// Refresh + copy-PNG controls shared by both activity chart cards.
function ChartActions({ onRefresh, onCopy, loading, copyState, refreshTitle, copyTitle, t }) {
  return (
    <>
      <button type="button" className="icon-button" onClick={onCopy}
        title={copyTitle} aria-label={t('chart.copyPng', 'Copy chart as PNG')}>
        {copyState === 'copied' || copyState === 'downloaded' ? (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12" /></svg>
        ) : (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></svg>
        )}
      </button>
      <button type="button" className="icon-button" onClick={onRefresh} title={refreshTitle} aria-label={t('common.refresh')} disabled={loading}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={loading ? { animation: 'spin 1s linear infinite' } : undefined}>
          <polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" />
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
        </svg>
      </button>
    </>
  );
}

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

function Kpi({ label, value, tone = '', onClick, tip }) {
  return (
    <button type="button" className={`kpi-tile ${onClick ? 'clickable' : ''}`} onClick={onClick} style={!onClick ? { cursor: 'default' } : undefined}
      title={tip || (onClick ? `${label} — click to open.` : label)}>
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
  // Range is shared by both activity charts so they stay in lockstep.
  const [rangeId, setRangeId] = useState('day');
  const [summary, setSummary] = useState(null);
  const [chartLoading, setChartLoading] = useState(false);
  const [error, setError] = useState(null);

  const [ingestion, setIngestion] = useState(null);
  const [ingestionLoading, setIngestionLoading] = useState(false);
  const [ingestionError, setIngestionError] = useState(null);
  const [subjects, setSubjects] = useState([]);
  const [ingestionSubjectId, setIngestionSubjectId] = useState('');

  const [copyState, setCopyState] = useState({});
  const reqChartRef = useRef(null);
  const ingChartRef = useRef(null);

  const loadCounts = useCallback(async () => {
    const [tenants, users, subjectsCount, links, jobsQueued, jobsFailed] = await Promise.all([
      countOf(apiClient.list('tenants', { maxResults: 1 })),
      countOf(apiClient.list('users', { maxResults: 1 })),
      countOf(apiClient.list('subjects', { maxResults: 1 })),
      countOf(apiClient.list('links', { maxResults: 1 })),
      countOf(apiClient.list('jobs', { status: 'Queued', maxResults: 1 })),
      countOf(apiClient.list('jobs', { status: 'Failed', maxResults: 1 }))
    ]);
    setCounts({ tenants, users, subjects: subjectsCount, links, jobsQueued, jobsFailed });
  }, [apiClient]);

  const loadSubjects = useCallback(async () => {
    try {
      const resp = await apiClient.list('subjects', { maxResults: 1000 });
      setSubjects(normalizeList(resp).items);
    } catch {
      /* subject filter is best-effort; leave it empty */
    }
  }, [apiClient]);

  const loadSummary = useCallback(async () => {
    setChartLoading(true);
    setError(null);
    try {
      const resp = await apiClient.getRequestHistorySummary(rangeToParams(rangeId));
      setSummary(resp);
    } catch (err) {
      setError(err?.message || 'Failed to load request activity');
      setSummary(null);
    } finally {
      setChartLoading(false);
    }
  }, [apiClient, rangeId]);

  const loadIngestion = useCallback(async () => {
    setIngestionLoading(true);
    setIngestionError(null);
    try {
      const params = rangeToParams(rangeId);
      if (ingestionSubjectId) params.subjectId = ingestionSubjectId;
      const resp = await apiClient.getIngestionSummary(params);
      setIngestion(resp);
    } catch (err) {
      setIngestionError(err?.message || 'Failed to load ingestion activity');
      setIngestion(null);
    } finally {
      setIngestionLoading(false);
    }
  }, [apiClient, rangeId, ingestionSubjectId]);

  useEffect(() => { loadCounts(); }, [loadCounts]);
  useEffect(() => { loadSubjects(); }, [loadSubjects]);
  useEffect(() => { loadSummary(); }, [loadSummary]);
  useEffect(() => { loadIngestion(); }, [loadIngestion]);

  const totals = {
    total: summary?.totalCount ?? summary?.TotalCount ?? 0,
    success: summary?.totalSuccess ?? summary?.TotalSuccess ?? 0,
    failure: summary?.totalFailure ?? summary?.TotalFailure ?? 0
  };
  const ingestionTotal = ingestion?.totalCount ?? ingestion?.TotalCount ?? 0;

  const handleBucketClick = (bucket) => {
    navigate(`/dashboard/requests?fromUtc=${encodeURIComponent(bucket.startUtc)}&toUtc=${encodeURIComponent(bucket.endUtc)}`);
  };

  const flashCopy = (key, result) => {
    setCopyState((prev) => ({ ...prev, [key]: result }));
    window.setTimeout(() => setCopyState((prev) => ({ ...prev, [key]: null })), 1800);
  };

  const copyRequestChart = async () => {
    const svg = reqChartRef.current?.querySelector('svg');
    const result = await copyChartPng(svg, {
      title: t('home.activity'),
      xLabel: t('chart.axisTime', 'Time'),
      yLabel: t('chart.axisRequests', 'Requests'),
      legend: [
        { label: t('chart.successLegend'), color: getComputedStyle(document.documentElement).getPropertyValue('--color-success').trim() || '#16a34a' },
        { label: t('chart.failedLegend'), color: getComputedStyle(document.documentElement).getPropertyValue('--color-danger').trim() || '#dc2626' }
      ]
    });
    flashCopy('req', result);
  };

  const copyIngestionChart = async () => {
    const svg = ingChartRef.current?.querySelector('svg');
    const present = stagesPresent(normalizeIngestionBuckets(ingestion));
    const result = await copyChartPng(svg, {
      title: t('home.ingestionActivity', 'Ingestion Activity'),
      xLabel: t('chart.axisTime', 'Time'),
      yLabel: t('chart.axisStageEvents', 'Stage events'),
      legend: present.map((s) => ({ label: stageLabel(s), color: stageColor(s) }))
    });
    flashCopy('ing', result);
  };

  return (
    <div>
      <PageHeader title={t('home.title')} subtitle={t('home.subtitle')} />

      <div className="kpi-grid">
        <Kpi label={t('home.kpiTenants')} value={counts.tenants} onClick={() => navigate('/dashboard/tenants')} tip="Total tenants configured. Click to manage them." />
        <Kpi label={t('home.kpiUsers')} value={counts.users} onClick={() => navigate('/dashboard/users')} tip="Total user accounts across tenants. Click to manage them." />
        <Kpi label={t('home.kpiSubjects')} value={counts.subjects} onClick={() => navigate('/dashboard/subjects')} tip="Subjects (knowledge archives) defined. Click to manage them." />
        <Kpi label={t('home.kpiLinks')} value={counts.links} onClick={() => navigate('/dashboard/links')} tip="Source links submitted for ingestion. Click to view them." />
        <Kpi label={t('home.kpiJobsQueued')} value={counts.jobsQueued} onClick={() => navigate('/dashboard/jobs')} tip="Ingestion jobs waiting or in progress. Click to open the queue." />
        <Kpi label={t('home.kpiJobsFailed')} value={counts.jobsFailed} tone="danger" onClick={() => navigate('/dashboard/jobs')} tip="Ingestion jobs that failed and may need attention. Click to review them." />
      </div>

      {error && <ErrorBanner message={error} onRetry={loadSummary} onDismiss={() => setError(null)} />}
      {ingestionError && <ErrorBanner message={ingestionError} onRetry={loadIngestion} onDismiss={() => setIngestionError(null)} />}

      <div className="chart-range-bar">
        <span className="chart-range-label">{t('chart.rangeLabel', 'Time range')}</span>
        <ChartRangeControls value={rangeId} onChange={setRangeId} />
      </div>

      <div className="chart-card" style={{ marginBottom: 'var(--spacing-lg)' }}>
        <div className="chart-header">
          <h2>{t('home.activity')}</h2>
          <div className="chart-controls">
            <ChartActions t={t} loading={chartLoading} copyState={copyState.req}
              onRefresh={loadSummary} onCopy={copyRequestChart}
              refreshTitle="Reload the request activity chart with the latest data."
              copyTitle={t('chart.copyPngHint', 'Copy this chart as a PNG (with title and axis labels).')} />
          </div>
        </div>
        <div className="chart-stats">
          <div className="chart-stat"><span className="chart-stat-value">{formatNumber(totals.total)}</span><span className="chart-stat-label">{t('chart.total')}</span></div>
          <div className="chart-stat"><span className="chart-stat-value" style={{ color: 'var(--color-success)' }}>{formatNumber(totals.success)}</span><span className="chart-stat-label">{t('chart.success')}</span></div>
          <div className="chart-stat"><span className="chart-stat-value" style={{ color: 'var(--color-danger)' }}>{formatNumber(totals.failure)}</span><span className="chart-stat-label">{t('chart.failed')}</span></div>
        </div>
        <div ref={reqChartRef}>
          <ActivityChart summary={summary} rangeId={rangeId} onBucketClick={handleBucketClick} />
        </div>
      </div>

      <div className="chart-card" style={{ marginBottom: 'var(--spacing-lg)' }}>
        <div className="chart-header">
          <h2>{t('home.ingestionActivity', 'Ingestion Activity')}</h2>
          <div className="chart-controls">
            <select className="chart-subject-select" value={ingestionSubjectId} onChange={(e) => setIngestionSubjectId(e.target.value)}
              title={t('chart.subjectFilterHint', 'Limit ingestion activity to a single subject.')} aria-label={t('chart.subjectFilter', 'Subject')}>
              <option value="">{t('chart.allSubjects', 'All subjects')}</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>
              ))}
            </select>
            <ChartActions t={t} loading={ingestionLoading} copyState={copyState.ing}
              onRefresh={loadIngestion} onCopy={copyIngestionChart}
              refreshTitle="Reload the ingestion activity chart with the latest data."
              copyTitle={t('chart.copyPngHint', 'Copy this chart as a PNG (with title and axis labels).')} />
          </div>
        </div>
        <div className="chart-stats">
          <div className="chart-stat"><span className="chart-stat-value">{formatNumber(ingestionTotal)}</span><span className="chart-stat-label">{t('chart.stageEventsTotal', 'Stage events')}</span></div>
        </div>
        <div ref={ingChartRef}>
          <IngestionActivityChart summary={ingestion} rangeId={rangeId} />
        </div>
      </div>

      <div className="section">
        <h2>{t('home.quickLinks')}</h2>
        <div className="quick-links">
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/subjects')} title="Manage subjects — the archives your content is organized under."><Icon name="users" /> {t('nav.subjects')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/jobs')} title="Open the ingestion queue to watch jobs process."><Icon name="queue" /> {t('nav.jobs')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/model-runners')} title="Configure the embedding and completion model endpoints."><Icon name="cpu" /> {t('nav.modelRunners')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/explorer')} title="Try API calls interactively in the OpenAPI explorer."><Icon name="play" /> {t('nav.explorer')}</button>
          <button type="button" className="quick-link" onClick={() => navigate('/dashboard/requests')} title="Inspect captured API request history."><Icon name="chart" /> {t('nav.requests')}</button>
        </div>
      </div>

      <div className="section">
        <h2>{t('home.systems')}</h2>
        <div className="systems-grid">
          {SYSTEMS.map((sys) => (
            <a key={sys.name} className="system-card" href={sys.url} target="_blank" rel="noopener noreferrer"
              title={`Open the ${sys.name} console at ${sys.url} in a new tab.`}>
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
