import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { buildRangeParams } from '../utils/activity';
import { formatNumber, formatRelativeTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import ActivityChart from '../components/ActivityChart';
import IngestionActivityChart from '../components/IngestionActivityChart';
import ChartRangeControls from '../components/ChartRangeControls';
import StatusPill from '../components/StatusPill';
import { copyChartPng } from '../utils/chartExport';
import { normalizeIngestionBuckets, stagesPresent, stageColor, stageLabel } from '../utils/ingestionActivity';

// Refresh + copy-PNG controls shared by both activity chart cards.
function ChartActions({ onRefresh, onCopy, loading, copyState }) {
  return (
    <div style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
      <button className="btn-icon" onClick={onCopy} title="Copy this chart as a PNG (with title and axis labels)." aria-label="Copy chart as PNG">
        {copyState === 'copied' || copyState === 'downloaded' ? (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12" /></svg>
        ) : (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></svg>
        )}
      </button>
      <button className="btn-icon" onClick={onRefresh} disabled={loading} title="Refresh" aria-label="Refresh">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={loading ? { animation: 'spin 0.8s linear infinite' } : undefined}>
          <path d="M23 4v6h-6" /><path d="M1 20v-6h6" />
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
        </svg>
      </button>
    </div>
  );
}

function isProcessing(status) {
  const s = (status || '').toLowerCase();
  return s === 'processing' || s === 'running' || s === 'queued' || s === 'submitted' || s === 'pending';
}
function isFailed(status) {
  const s = (status || '').toLowerCase();
  return s === 'failed' || s === 'error';
}

function HomeView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [subjects, setSubjects] = useState([]);
  const [links, setLinks] = useState([]);
  const [jobs, setJobs] = useState([]);
  const [summary, setSummary] = useState(null);
  // Range is shared by both activity charts so they stay in lockstep.
  const [rangeId, setRangeId] = useState('day');
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [refreshKey, setRefreshKey] = useState(0);

  const [ingestion, setIngestion] = useState(null);
  const [ingestionLoading, setIngestionLoading] = useState(true);
  const [ingestionSubjectId, setIngestionSubjectId] = useState('');
  const [ingestionRefreshKey, setIngestionRefreshKey] = useState(0);
  const [copyState, setCopyState] = useState({});
  const reqChartRef = useRef(null);
  const ingChartRef = useRef(null);

  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    (async () => {
      const [c, l, j] = await Promise.allSettled([
        apiClient.getSubjects({ maxResults: 1000 }),
        apiClient.getLinks({ maxResults: 1000 }),
        apiClient.getJobs({ maxResults: 1000 })
      ]);
      if (cancelled) return;
      if (c.status === 'fulfilled') setSubjects(asArray(c.value, 'subjects'));
      if (l.status === 'fulfilled') setLinks(asArray(l.value, 'links'));
      if (j.status === 'fulfilled') setJobs(asArray(j.value, 'jobs'));
    })();
    return () => {
      cancelled = true;
    };
  }, [apiClient, refreshKey]);

  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    setSummaryLoading(true);
    apiClient
      .getRequestHistorySummary(buildRangeParams(rangeId))
      .then((res) => {
        if (!cancelled) setSummary(res || null);
      })
      .catch(() => {
        if (!cancelled) setSummary(null);
      })
      .finally(() => {
        if (!cancelled) setSummaryLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [apiClient, rangeId, refreshKey]);

  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    setIngestionLoading(true);
    const params = buildRangeParams(rangeId);
    if (ingestionSubjectId) params.subjectId = ingestionSubjectId;
    apiClient
      .getIngestionSummary(params)
      .then((res) => {
        if (!cancelled) setIngestion(res || null);
      })
      .catch(() => {
        if (!cancelled) setIngestion(null);
      })
      .finally(() => {
        if (!cancelled) setIngestionLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [apiClient, rangeId, ingestionSubjectId, ingestionRefreshKey]);

  const flashCopy = (key, result) => {
    setCopyState((prev) => ({ ...prev, [key]: result }));
    window.setTimeout(() => setCopyState((prev) => ({ ...prev, [key]: null })), 1800);
  };

  const copyRequestChart = async () => {
    const svg = reqChartRef.current?.querySelector('svg');
    const readVar = (n, f) => getComputedStyle(document.documentElement).getPropertyValue(n).trim() || f;
    const result = await copyChartPng(svg, {
      title: t('home.activity', 'Request Activity'),
      xLabel: 'Time',
      yLabel: 'Requests',
      legend: [
        { label: 'Success', color: readVar('--color-success', '#16a34a') },
        { label: 'Failed', color: readVar('--color-danger', '#dc2626') }
      ]
    });
    flashCopy('req', result);
  };

  const copyIngestionChart = async () => {
    const svg = ingChartRef.current?.querySelector('svg');
    const present = stagesPresent(normalizeIngestionBuckets(ingestion));
    const result = await copyChartPng(svg, {
      title: t('home.ingestionActivity', 'Ingestion Activity'),
      xLabel: 'Time',
      yLabel: 'Stage events',
      legend: present.map((s) => ({ label: stageLabel(s), color: stageColor(s) }))
    });
    flashCopy('ing', result);
  };

  const processingCount = jobs.filter((j) => isProcessing(j.status)).length;
  const failedJobs = jobs.filter((j) => isFailed(j.status));
  const failedLinks = links.filter((l) => isFailed(l.status));
  const recentFailures = [...failedLinks].slice(0, 6);

  const handleBucketClick = useCallback(
    (bucket) => {
      navigate(`/dashboard/requests?fromUtc=${bucket.bucketStartUtc}&toUtc=${bucket.bucketEndUtc}`);
    },
    [navigate]
  );

  const kpis = [
    { label: t('home.kpiSubjects'), value: subjects.length, section: 'subjects' },
    { label: t('home.kpiLinks'), value: links.length, section: 'links' },
    { label: t('home.kpiProcessing'), value: processingCount, section: 'ingestion', accent: 'warning' },
    { label: t('home.kpiFailed'), value: failedJobs.length, section: 'ingestion', accent: 'danger' }
  ];

  return (
    <div>
      <PageHeader title={t('home.title')} subtitle={t('home.subtitle')} />

      <div className="kpi-grid">
        {kpis.map((kpi) => (
          <div
            key={kpi.label}
            className="kpi-tile clickable"
            onClick={() => navigate(`/dashboard/${kpi.section}`)}
          >
            <div className="kpi-label">{kpi.label}</div>
            <div className={`kpi-value ${kpi.accent ? `accent-${kpi.accent}` : ''}`}>
              {formatNumber(kpi.value)}
            </div>
          </div>
        ))}
      </div>

      <div className="chart-range-bar">
        <span className="chart-range-label">Time range</span>
        <ChartRangeControls value={rangeId} onChange={setRangeId} />
      </div>

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('home.activity')}</h3>
          <ChartActions
            onRefresh={() => setRefreshKey((k) => k + 1)}
            onCopy={copyRequestChart}
            loading={summaryLoading}
            copyState={copyState.req}
          />
        </div>
        <div className="card-body">
          {summaryLoading && !summary ? (
            <div className="loading-block">
              <span className="loading-spinner" /> {t('common.loading')}
            </div>
          ) : (
            <div ref={reqChartRef}>
              <ActivityChart summary={summary} rangeId={rangeId} onBucketClick={handleBucketClick} />
            </div>
          )}
        </div>
      </div>

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('home.ingestionActivity', 'Ingestion Activity')}</h3>
          <div style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'nowrap' }}>
            <select className="chart-subject-select" value={ingestionSubjectId} onChange={(e) => setIngestionSubjectId(e.target.value)}
              title="Limit ingestion activity to a single subject." aria-label="Subject">
              <option value="">All subjects</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>
              ))}
            </select>
            <ChartActions
              onRefresh={() => setIngestionRefreshKey((k) => k + 1)}
              onCopy={copyIngestionChart}
              loading={ingestionLoading}
              copyState={copyState.ing}
            />
          </div>
        </div>
        <div className="card-body">
          {ingestionLoading && !ingestion ? (
            <div className="loading-block">
              <span className="loading-spinner" /> {t('common.loading')}
            </div>
          ) : (
            <div ref={ingChartRef}>
              <IngestionActivityChart summary={ingestion} rangeId={rangeId} />
            </div>
          )}
        </div>
      </div>

      <div className="card">
        <div className="card-header">
          <h3>{t('home.recentFailures')}</h3>
          <div className="page-header-actions">
            <button className="btn btn-secondary btn-sm" onClick={() => navigate('/dashboard/links')}>
              {t('home.submitLinkCta')}
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => navigate('/dashboard/ingestion')}>
              {t('home.viewIngestionCta')}
            </button>
          </div>
        </div>
        <div className="card-body">
          {recentFailures.length === 0 ? (
            <p className="empty-state-description" style={{ margin: 0 }}>
              {t('home.noFailures')}
            </p>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{t('links.linkTitle')}</th>
                    <th>{t('links.url')}</th>
                    <th>{t('common.status')}</th>
                    <th>{t('links.lastError')}</th>
                    <th>{t('links.lastIngested')}</th>
                  </tr>
                </thead>
                <tbody>
                  {recentFailures.map((link) => (
                    <tr key={link.id} className="clickable-row" onClick={() => navigate('/dashboard/links')}>
                      <td>{link.title || '(untitled)'}</td>
                      <td className="cell-url" title={link.url}>{link.url}</td>
                      <td><StatusPill status={link.status} /></td>
                      <td className="cell-error" title={link.lastError}>{link.lastError || '—'}</td>
                      <td>{link.lastIngestedUtc ? formatRelativeTime(link.lastIngestedUtc) : t('common.never')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default HomeView;
