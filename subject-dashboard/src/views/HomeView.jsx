import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { buildRangeParams } from '../utils/activity';
import { formatNumber, formatRelativeTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import ActivityChart from '../components/ActivityChart';
import ChartRangeControls from '../components/ChartRangeControls';
import StatusPill from '../components/StatusPill';

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
  const [rangeId, setRangeId] = useState('day');
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [refreshKey, setRefreshKey] = useState(0);

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

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('home.activity')}</h3>
          <ChartRangeControls
            value={rangeId}
            onChange={setRangeId}
            onRefresh={() => setRefreshKey((k) => k + 1)}
            loading={summaryLoading}
          />
        </div>
        <div className="card-body">
          {summaryLoading && !summary ? (
            <div className="loading-block">
              <span className="loading-spinner" /> {t('common.loading')}
            </div>
          ) : (
            <ActivityChart summary={summary} rangeId={rangeId} onBucketClick={handleBucketClick} />
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
