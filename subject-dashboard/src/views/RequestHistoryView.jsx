import { useState, useEffect, useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { buildRangeParams, summaryTotals } from '../utils/activity';
import { formatDateTime, formatDurationMs, formatNumber, formatPercent } from '../utils/format';
import PageHeader from '../components/PageHeader';
import Pagination from '../components/Pagination';
import ActivityChart from '../components/ActivityChart';
import ChartRangeControls from '../components/ChartRangeControls';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';
import JsonViewer from '../components/JsonViewer';

function toLocalInput(date) {
  const offset = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}
function toIso(value) {
  return value ? new Date(value).toISOString() : '';
}

function readMethod(e) {
  return e.method || e.httpMethod || e.verb || '';
}
function readPath(e) {
  return e.path || e.routeTemplate || e.route || e.url || '';
}
function readStatus(e) {
  return e.statusCode ?? e.status ?? e.responseStatus;
}
function isSuccess(e) {
  const code = Number(readStatus(e));
  if (!Number.isNaN(code) && code > 0) return code < 400;
  return e.success !== false;
}
function readCreated(e) {
  return e.createdUtc || e.timestampUtc || e.startedUtc || e.timeUtc;
}

function RequestHistoryView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const initialFrom = searchParams.get('fromUtc');
  const initialTo = searchParams.get('toUtc');

  const [entries, setEntries] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [refreshKey, setRefreshKey] = useState(0);

  const [summary, setSummary] = useState(null);
  const [rangeId, setRangeId] = useState('day');
  const [summaryLoading, setSummaryLoading] = useState(true);

  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const [filters, setFilters] = useState({
    method: '',
    statusCode: '',
    pathContains: '',
    fromUtc: initialFrom ? toLocalInput(new Date(initialFrom)) : toLocalInput(new Date(Date.now() - 86400000)),
    toUtc: initialTo ? toLocalInput(new Date(initialTo)) : toLocalInput(new Date())
  });

  const queryParams = useMemo(
    () => ({
      method: filters.method,
      statusCode: filters.statusCode,
      pathContains: filters.pathContains,
      fromUtc: toIso(filters.fromUtc),
      toUtc: toIso(filters.toUtc),
      pageNumber: page,
      pageSize
    }),
    [filters, page, pageSize]
  );

  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    setLoading(true);
    apiClient
      .getRequestHistory(queryParams)
      .then((res) => {
        if (cancelled) return;
        setEntries(asArray(res, 'entries'));
        setTotalCount(res?.totalCount ?? res?.total ?? asArray(res, 'entries').length);
        setError('');
      })
      .catch((err) => {
        if (!cancelled) setError(err.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [apiClient, queryParams, refreshKey]);

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

  const totals = summaryTotals(summary);
  const successRate = totals.total ? formatPercent((totals.success / totals.total) * 100) : '0%';
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  const setFilter = (key, value) => {
    setFilters((f) => ({ ...f, [key]: value }));
    setPage(1);
  };

  const resetFilters = () => {
    setFilters({
      method: '',
      statusCode: '',
      pathContains: '',
      fromUtc: toLocalInput(new Date(Date.now() - 86400000)),
      toUtc: toLocalInput(new Date())
    });
    setPage(1);
  };

  const openDetail = async (entry) => {
    setDetail({ _row: entry });
    setDetailLoading(true);
    try {
      const full = await apiClient.getRequestHistoryEntry(entry.id);
      setDetail({ ...full, _row: entry });
    } catch (err) {
      setError(err.message);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await apiClient.deleteRequestHistoryEntry(deleteTarget.id);
      setDeleteTarget(null);
      setRefreshKey((k) => k + 1);
    } catch (err) {
      setError(err.message);
    } finally {
      setDeleting(false);
    }
  };

  const refresh = useCallback(() => setRefreshKey((k) => k + 1), []);

  const kpis = [
    { label: t('requests.total'), value: formatNumber(totals.total) },
    { label: t('requests.successRate'), value: summaryLoading ? '...' : successRate },
    { label: t('requests.failed'), value: formatNumber(totals.failure), accent: 'danger' },
    { label: t('requests.avgDuration'), value: formatDurationMs(totals.avg) }
  ];

  return (
    <div>
      <PageHeader
        title={t('requests.title')}
        subtitle={t('requests.subtitle')}
        actions={
          <button className="btn btn-secondary" onClick={resetFilters}>
            {t('requests.resetFilters')}
          </button>
        }
      />

      {error && <div className="error-banner">{error}</div>}

      <div className="kpi-grid">
        {kpis.map((kpi) => (
          <div className="kpi-tile" key={kpi.label}>
            <div className="kpi-label">{kpi.label}</div>
            <div className={`kpi-value ${kpi.accent ? `accent-${kpi.accent}` : ''}`}>{kpi.value}</div>
          </div>
        ))}
      </div>

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('home.activity')}</h3>
          <ChartRangeControls value={rangeId} onChange={setRangeId} onRefresh={refresh} loading={summaryLoading} />
        </div>
        <div className="card-body">
          {summaryLoading && !summary ? (
            <div className="loading-block"><span className="loading-spinner" /></div>
          ) : (
            <ActivityChart summary={summary} rangeId={rangeId} />
          )}
        </div>
      </div>

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('common.search')}</h3>
        </div>
        <div className="card-body">
          <div className="form-row" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))' }}>
            <div className="form-group" style={{ marginBottom: 0 }}>
              <label>{t('requests.method')}</label>
              <select value={filters.method} onChange={(e) => setFilter('method', e.target.value)}>
                <option value="">{t('common.all')}</option>
                {['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'].map((m) => (
                  <option key={m} value={m}>{m}</option>
                ))}
              </select>
            </div>
            <div className="form-group" style={{ marginBottom: 0 }}>
              <label>Status Code</label>
              <input value={filters.statusCode} onChange={(e) => setFilter('statusCode', e.target.value)} placeholder="500" />
            </div>
            <div className="form-group" style={{ marginBottom: 0 }}>
              <label>{t('requests.path')}</label>
              <input value={filters.pathContains} onChange={(e) => setFilter('pathContains', e.target.value)} placeholder="/v1.0/links" />
            </div>
            <div className="form-group" style={{ marginBottom: 0 }}>
              <label>From</label>
              <input type="datetime-local" value={filters.fromUtc} onChange={(e) => setFilter('fromUtc', e.target.value)} />
            </div>
            <div className="form-group" style={{ marginBottom: 0 }}>
              <label>To</label>
              <input type="datetime-local" value={filters.toUtc} onChange={(e) => setFilter('toUtc', e.target.value)} />
            </div>
          </div>
        </div>
      </div>

      <div className="table-frame">
        <Pagination
          currentPage={page}
          totalPages={totalPages}
          pageSize={pageSize}
          totalItems={totalCount}
          onPageChange={setPage}
          onPageSizeChange={(s) => {
            setPageSize(s);
            setPage(1);
          }}
          onRefresh={refresh}
        />
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>{t('requests.when')}</th>
                <th>{t('requests.method')}</th>
                <th>{t('requests.path')}</th>
                <th>{t('common.status')}</th>
                <th>{t('requests.duration')}</th>
                <th className="actions-column">{t('common.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={6} className="table-loading"><span className="loading-spinner" /></td></tr>
              ) : entries.length === 0 ? (
                <tr><td colSpan={6} className="table-empty">{t('requests.empty')}</td></tr>
              ) : (
                entries.map((e) => {
                  const ok = isSuccess(e);
                  return (
                    <tr key={e.id} className="clickable-row" onClick={() => openDetail(e)}>
                      <td>{formatDateTime(readCreated(e))}</td>
                      <td>
                        <span className={`method-pill method-${readMethod(e).toLowerCase()}`}>{readMethod(e)}</span>
                      </td>
                      <td className="cell-mono" title={readPath(e)}>{readPath(e)}</td>
                      <td>
                        <span className={`status-pill ${ok ? 'pill-success' : 'pill-danger'}`}>{readStatus(e)}</span>
                      </td>
                      <td>{formatDurationMs(e.durationMs)}</td>
                      <td className="actions-column" onClick={(ev) => ev.stopPropagation()}>
                        <ActionMenu
                          actions={[
                            { label: t('common.view'), onClick: () => openDetail(e) },
                            { label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(e) }
                          ]}
                        />
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      <Modal isOpen={!!detail} onClose={() => setDetail(null)} title={t('requests.detail')} size="fullscreen">
        {detailLoading ? (
          <div className="loading-block"><span className="loading-spinner" /> {t('common.loading')}</div>
        ) : detail ? (
          <div>
            <div className="detail-grid" style={{ marginBottom: 18 }}>
              <div className="detail-item">
                <span className="detail-label">ID</span>
                <span className="detail-value"><CopyableId value={detail._row?.id} /></span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('requests.method')}</span>
                <span className="detail-value">{readMethod(detail._row || {})}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('requests.path')}</span>
                <span className="detail-value cell-mono">{readPath(detail._row || {})}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('common.status')}</span>
                <span className="detail-value">{readStatus(detail._row || {})}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('requests.when')}</span>
                <span className="detail-value">{formatDateTime(readCreated(detail._row || {}))}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('requests.duration')}</span>
                <span className="detail-value">{formatDurationMs(detail._row?.durationMs)}</span>
              </div>
            </div>
            <div style={{ display: 'grid', gap: 16 }}>
              <JsonViewer value={detail.requestHeaders || detail.requestHeadersJson || {}} label="Request Headers" />
              <JsonViewer value={detail.requestBody ?? '(empty)'} label="Request Body" />
              <JsonViewer value={detail.responseHeaders || detail.responseHeadersJson || {}} label="Response Headers" />
              <JsonViewer value={detail.responseBody ?? '(empty)'} label="Response Body" />
              <JsonViewer value={detail} label="Raw JSON" />
            </div>
          </div>
        ) : null}
      </Modal>

      <ConfirmModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('common.delete')}
        message="Delete this request history entry?"
        entityName={deleteTarget ? readPath(deleteTarget) : ''}
        confirmLabel={t('common.delete')}
        isLoading={deleting}
      />
    </div>
  );
}

export default RequestHistoryView;
