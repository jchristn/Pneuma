import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import ActivityChart, { RANGES, rangeToParams } from '../components/ActivityChart';
import RequestDetailsModal from '../components/RequestDetailsModal';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewer from '../components/JsonViewer';
import ErrorBanner from '../components/ErrorBanner';
import StatusPill, { toneForHttpStatus, toneForMethod } from '../components/StatusPill';
import { getId } from '../components/ResourceView';
import { formatDateTime, formatDuration, formatNumber } from '../i18n/formatters';

const METHODS = ['', 'GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'];

function RequestHistoryView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [searchParams] = useSearchParams();

  const [filters, setFilters] = useState({
    method: '',
    statusCode: '',
    pathContains: '',
    fromUtc: searchParams.get('fromUtc') || '',
    toUtc: searchParams.get('toUtc') || ''
  });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [rangeId, setRangeId] = useState('day');
  const [summary, setSummary] = useState(null);
  const [chartLoading, setChartLoading] = useState(false);

  const [modal, setModal] = useState(null);

  const buildQuery = useCallback(() => {
    const q = { pageNumber, pageSize };
    Object.entries(filters).forEach(([k, v]) => { if (v !== '') q[k] = v; });
    return q;
  }, [filters, pageNumber, pageSize]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.getRequestHistory(buildQuery());
      const norm = normalizeList(resp);
      setRows(norm.items);
      setTotalCount(norm.totalCount);
    } catch (err) {
      setError(err?.message || 'Failed to load request history');
      setRows([]);
      setTotalCount(0);
    } finally {
      setLoading(false);
    }
  }, [apiClient, buildQuery]);

  const loadSummary = useCallback(async () => {
    setChartLoading(true);
    try {
      const resp = await apiClient.getRequestHistorySummary(rangeToParams(rangeId));
      setSummary(resp);
    } catch {
      setSummary(null);
    } finally {
      setChartLoading(false);
    }
  }, [apiClient, rangeId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadSummary(); }, [loadSummary]);

  const applyFilters = () => { setPageNumber(1); load(); };
  const clearFilters = () => {
    setFilters({ method: '', statusCode: '', pathContains: '', fromUtc: '', toUtc: '' });
    setPageNumber(1);
  };

  const setFilter = (k, v) => setFilters((prev) => ({ ...prev, [k]: v }));

  const handleBucketClick = (bucket) => {
    setFilters((prev) => ({ ...prev, fromUtc: bucket.startUtc.slice(0, 16), toUtc: bucket.endUtc.slice(0, 16) }));
    setPageNumber(1);
  };

  const totals = {
    total: summary?.totalCount ?? summary?.TotalCount ?? 0,
    success: summary?.totalSuccess ?? summary?.TotalSuccess ?? 0,
    failure: summary?.totalFailure ?? summary?.TotalFailure ?? 0
  };

  const columns = [
    { key: 'createdUtc', label: t('requests.time'), render: (r) => formatDateTime(r.createdUtc ?? r.CreatedUtc) },
    { key: 'method', label: t('requests.method'), render: (r) => <StatusPill label={r.method ?? r.Method} tone={toneForMethod(r.method ?? r.Method)} mono /> },
    { key: 'path', label: t('requests.path'), cellClass: 'wrap', render: (r) => <code className="cell-id" style={{ whiteSpace: 'normal' }}>{r.path ?? r.Path ?? r.routeTemplate}</code> },
    { key: 'statusCode', label: t('requests.status'), render: (r) => <StatusPill label={r.statusCode ?? r.StatusCode} tone={toneForHttpStatus(r.statusCode ?? r.StatusCode)} /> },
    { key: 'durationMs', label: t('requests.duration'), render: (r) => formatDuration(r.durationMs ?? r.DurationMs) },
    { key: 'principalName', label: t('requests.principal'), render: (r) => r.principalName ?? r.PrincipalName ?? r.userId ?? '—' },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (r) => (
      <ActionMenu items={[
        { key: 'view', label: t('common.view'), onClick: () => setModal({ type: 'view', id: getId(r) }) },
        { key: 'json', label: t('common.viewJson'), onClick: () => setModal({ type: 'json', item: r }) },
        { key: 'delete', label: t('common.delete'), danger: true, onClick: () => setModal({ type: 'delete', id: getId(r) }) }
      ]} />
    ) }
  ];

  const emptyMessage = Object.values(filters).some((v) => v !== '')
    ? t('requests.emptyNoMatch') : t('requests.emptyNoTraffic');

  return (
    <div>
      <PageHeader title={t('requests.title')} subtitle={t('requests.subtitle')} />

      <div className="kpi-grid">
        <div className="kpi-tile"><span className="kpi-label">{t('chart.total')}</span><span className="kpi-value">{formatNumber(totals.total)}</span></div>
        <div className="kpi-tile"><span className="kpi-label">{t('chart.success')}</span><span className="kpi-value success">{formatNumber(totals.success)}</span></div>
        <div className="kpi-tile"><span className="kpi-label">{t('chart.failed')}</span><span className="kpi-value danger">{formatNumber(totals.failure)}</span></div>
      </div>

      <div className="chart-card" style={{ marginBottom: 'var(--spacing-lg)' }}>
        <div className="chart-header">
          <h2>{t('home.activity')}</h2>
          <div className="chart-controls">
            <div className="segmented">
              {Object.keys(RANGES).map((id) => (
                <button key={id} type="button" className={rangeId === id ? 'active' : ''} onClick={() => setRangeId(id)}>{t(RANGES[id].labelKey)}</button>
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
        <ActivityChart summary={summary} rangeId={rangeId} onBucketClick={handleBucketClick} />
      </div>

      <div className="filter-bar">
        <div className="field">
          <label htmlFor="f-method" className="has-tip" title="Show only requests using this HTTP method. Leave blank for all methods.">{t('requests.method')}</label>
          <select id="f-method" value={filters.method} onChange={(e) => setFilter('method', e.target.value)} title="Show only requests using this HTTP method. Leave blank for all methods.">
            {METHODS.map((m) => <option key={m} value={m}>{m || t('common.none')}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="f-status" className="has-tip" title="Match an exact response status code (e.g. 500 to find server errors, 401 for auth failures).">{t('requests.statusCode')}</label>
          <input id="f-status" value={filters.statusCode} onChange={(e) => setFilter('statusCode', e.target.value)} placeholder="500" title="Match an exact response status code (e.g. 500 to find server errors, 401 for auth failures)." />
        </div>
        <div className="field" style={{ minWidth: '200px' }}>
          <label htmlFor="f-path" className="has-tip" title="Substring match on the request path — e.g. /v1.0/subjects to see all subject calls.">{t('requests.pathContains')}</label>
          <input id="f-path" value={filters.pathContains} onChange={(e) => setFilter('pathContains', e.target.value)} placeholder="/v1.0/..." title="Substring match on the request path — e.g. /v1.0/subjects to see all subject calls." />
        </div>
        <div className="field">
          <label htmlFor="f-from" className="has-tip" title="Only show requests captured at or after this local time.">{t('requests.from')}</label>
          <input id="f-from" type="datetime-local" value={filters.fromUtc} onChange={(e) => setFilter('fromUtc', e.target.value)} title="Only show requests captured at or after this local time." />
        </div>
        <div className="field">
          <label htmlFor="f-to" className="has-tip" title="Only show requests captured at or before this local time.">{t('requests.to')}</label>
          <input id="f-to" type="datetime-local" value={filters.toUtc} onChange={(e) => setFilter('toUtc', e.target.value)} title="Only show requests captured at or before this local time." />
        </div>
        <button type="button" className="button-primary" onClick={applyFilters} title="Apply these filters and reload the request list.">{t('common.apply')}</button>
        <button type="button" className="button-secondary" onClick={clearFilters} title="Reset all filters and show the full request history.">{t('common.clear')}</button>
      </div>

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        onRefresh={load}
        onRowClick={(r) => setModal({ type: 'view', id: getId(r) })}
        emptyMessage={emptyMessage}
        server={{
          totalCount,
          pageNumber,
          pageSize,
          onPageChange: setPageNumber,
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); }
        }}
      />

      {modal?.type === 'view' && (
        <RequestDetailsModal id={modal.id} onClose={() => setModal(null)} onDeleted={load} />
      )}
      {modal?.type === 'json' && <JsonViewer data={modal.item} onClose={() => setModal(null)} />}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('requests.deleteOne')} message={t('requests.deleteConfirm')}
          confirmLabel={t('common.delete')}
          onConfirm={async () => { await apiClient.deleteRequestHistoryEntry(modal.id); await load(); }}
          onClose={() => setModal(null)} />
      )}
    </div>
  );
}

export default RequestHistoryView;
