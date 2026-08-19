import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import Modal from './Modal';
import CopyButton from './CopyButton';
import CopyableId from './CopyableId';
import StatusPill, { toneForHttpStatus, toneForMethod } from './StatusPill';
import { formatDateTime, formatDuration } from '../i18n/formatters';

function Section({ title, children }) {
  const [open, setOpen] = useState(true);
  return (
    <div className="detail-section">
      <div className="detail-section-header" onClick={() => setOpen((v) => !v)}>
        <h3>{title}</h3>
        <span className="icon-button">{open ? '▾' : '▸'}</span>
      </div>
      {open && children}
    </div>
  );
}

function HeadersTable({ headers }) {
  const entries = headers ? Object.entries(headers) : [];
  if (entries.length === 0) return <p style={{ color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)' }}>—</p>;
  return (
    <dl className="kv-grid">
      {entries.map(([k, v]) => (
        <div key={k} style={{ display: 'contents' }}>
          <dt style={{ textTransform: 'none', fontFamily: 'var(--font-mono)' }}>{k}</dt>
          <dd style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-xs)' }}>{String(v)}</dd>
        </div>
      ))}
    </dl>
  );
}

function BodyBlock({ body, truncated, sizeBytes }) {
  if (body === null || body === undefined || body === '') {
    return <p style={{ color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)' }}>—</p>;
  }
  let text = typeof body === 'string' ? body : JSON.stringify(body, null, 2);
  try { text = JSON.stringify(JSON.parse(text), null, 2); } catch { /* not json */ }
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
        {truncated ? <span className="pill pill-warning">Truncated{sizeBytes ? ` · ${sizeBytes} B` : ''}</span> : <span />}
        <CopyButton value={text} />
      </div>
      <pre className="code-block">{text}</pre>
    </div>
  );
}

function RequestDetailsModal({ id, onClose, onDeleted }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [entry, setEntry] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    apiClient.getRequestHistoryEntry(id)
      .then((data) => { if (!cancelled) setEntry(data); })
      .catch((err) => { if (!cancelled) setError(err?.message || 'Failed to load request'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient, id]);

  const e = entry || {};
  const method = e.method || e.Method;
  const statusCode = e.statusCode ?? e.StatusCode;

  return (
    <Modal
      title={t('requests.inspector')}
      size="xl"
      headerExtra={<CopyButton value={String(id)} label="ID" />}
      onClose={onClose}
      footer={(
        <>
          {onDeleted && (
            <button type="button" className="button-danger" onClick={async () => { await apiClient.deleteRequestHistoryEntry(id); onDeleted(); onClose(); }}>
              {t('common.delete')}
            </button>
          )}
          <button type="button" className="button-secondary" onClick={onClose}>{t('common.close')}</button>
        </>
      )}
    >
      {loading && <div className="table-loading"><div className="loading-spinner" /></div>}
      {error && <div className="error-message">{error}</div>}
      {!loading && !error && (
        <>
          <Section title={t('requests.metadata')}>
            <dl className="kv-grid">
              <dt>ID</dt><dd><CopyableId value={id} /></dd>
              <dt>{t('requests.method')}</dt><dd><StatusPill label={method} tone={toneForMethod(method)} mono /></dd>
              <dt>{t('requests.path')}</dt><dd style={{ fontFamily: 'var(--font-mono)' }}>{e.path || e.Path}</dd>
              <dt>{t('requests.status')}</dt><dd><StatusPill label={statusCode} tone={toneForHttpStatus(statusCode)} /></dd>
              <dt>{t('requests.duration')}</dt><dd>{formatDuration(e.durationMs ?? e.DurationMs)}</dd>
              <dt>Created</dt><dd>{formatDateTime(e.createdUtc ?? e.CreatedUtc)}</dd>
              <dt>Completed</dt><dd>{formatDateTime(e.completedUtc ?? e.CompletedUtc)}</dd>
              <dt>Source IP</dt><dd>{e.sourceIp ?? e.SourceIp ?? '—'}</dd>
              <dt>{t('requests.principal')}</dt><dd>{e.principalName ?? e.PrincipalName ?? e.userId ?? '—'}</dd>
              <dt>Tenant</dt><dd><CopyableId value={e.tenantId ?? e.TenantId} /></dd>
            </dl>
          </Section>
          <Section title={t('requests.requestHeaders')}><HeadersTable headers={e.requestHeaders ?? e.RequestHeaders} /></Section>
          <Section title={t('requests.requestBody')}>
            <BodyBlock body={e.requestBody ?? e.RequestBody} truncated={e.requestBodyTruncated} sizeBytes={e.requestBodySize} />
          </Section>
          <Section title={t('requests.responseHeaders')}><HeadersTable headers={e.responseHeaders ?? e.ResponseHeaders} /></Section>
          <Section title={t('requests.responseBody')}>
            <BodyBlock body={e.responseBody ?? e.ResponseBody} truncated={e.responseBodyTruncated} sizeBytes={e.responseBodySize} />
          </Section>
          <Section title={t('requests.rawJson')}><BodyBlock body={entry} /></Section>
        </>
      )}
    </Modal>
  );
}

export default RequestDetailsModal;
