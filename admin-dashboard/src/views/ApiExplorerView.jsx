import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApiExplorer } from '../hooks/useApiExplorer';
import { isDestructive } from '../utils/openApi';
import { buildCodeSnippets } from '../utils/openApi';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';
import ConfirmModal from '../components/ConfirmModal';
import CopyButton from '../components/CopyButton';
import StatusPill, { toneForHttpStatus } from '../components/StatusPill';
import { formatDuration } from '../i18n/formatters';

function methodClass(method) {
  return `explorer-method m-${String(method).toLowerCase()}`;
}

// Standard enumeration/paging query parameters honored by every collection endpoint
// (server-side via ReadEnumerationQuery) but not declared per-route in the OpenAPI spec.
const PAGING_PARAMS = [
  { name: 'maxResults', schema: { type: 'integer' }, description: 'Page size (max records to return).' },
  { name: 'skip', schema: { type: 'integer' }, description: 'Number of records to skip.' },
  { name: 'order', schema: { type: 'string' }, description: 'asc or desc by creation time.' },
  { name: 'search', schema: { type: 'string' }, description: 'Optional case-insensitive name filter.' }
];

// A collection GET (enumeration) endpoint: GET whose path does not end in a path parameter.
function isEnumerationOperation(operation) {
  return !!operation && operation.method === 'GET' && !/\}\s*$/.test(operation.path);
}

function ParamRows({ params, values, onChange }) {
  if (!params || params.length === 0) return <p style={{ color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)' }}>—</p>;
  return params.map((p) => (
    <div className="explorer-param-row" key={p.name}>
      <label title={p.description || ''}>{p.name}{p.required ? ' *' : ''}</label>
      <input value={values[p.name] ?? ''} onChange={(e) => onChange({ ...values, [p.name]: e.target.value })} placeholder={p.schema?.type || 'string'} />
    </div>
  ));
}

function ApiExplorerView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const ex = useApiExplorer(apiClient);
  const [tab, setTab] = useState('body');
  const [confirm, setConfirm] = useState(false);
  const [jsonError, setJsonError] = useState('');
  const [showHistory, setShowHistory] = useState(false);

  const pathParamDefs = (ex.operation?.parameters || []).filter((p) => p.in === 'path');
  const queryParamDefs = (ex.operation?.parameters || []).filter((p) => p.in === 'query');
  const hasBody = ex.operation && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(ex.operation.method);

  // For enumeration endpoints, offer paging inputs (skip, maxResults, order, search)
  // that the spec does not declare but the server honors.
  const declaredQueryNames = new Set(queryParamDefs.map((p) => p.name));
  const pagingParamDefs = isEnumerationOperation(ex.operation)
    ? PAGING_PARAMS.filter((p) => !declaredQueryNames.has(p.name))
    : [];

  const validateAndRun = () => {
    setJsonError('');
    if (hasBody && ex.body) {
      try { JSON.parse(ex.body); } catch { setJsonError(t('explorer.invalidJson')); return; }
    }
    if (ex.operation && isDestructive(ex.operation.method, ex.operation.path)) {
      setConfirm(true);
    } else {
      ex.execute();
    }
  };

  const snippets = ex.operation ? buildCodeSnippets({
    method: ex.operation.method, url: ex.resolvedUrl,
    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer <token>', ...ex.headers },
    body: hasBody ? ex.body : ''
  }) : { curl: '', fetch: '' };

  return (
    <div>
      <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} actions={(
        <button type="button" className="button-secondary" onClick={() => setShowHistory((v) => !v)}>{t('explorer.history')} ({ex.history.length})</button>
      )} />

      {ex.specError && <ErrorBanner message={`${t('explorer.noSpec')} (${ex.specError})`} />}

      {showHistory && (
        <div className="section">
          <h2>{t('explorer.history')}</h2>
          {ex.history.length === 0 ? <p style={{ color: 'var(--color-text-secondary)' }}>{t('common.noData')}</p> : ex.history.map((h) => (
            <div className="explorer-history-item" key={h.id}>
              <span><span className={methodClass(h.method)}>{h.method}</span> <code>{h.path}</code> <StatusPill label={h.status} tone={toneForHttpStatus(h.status)} /></span>
              <span style={{ display: 'flex', gap: '0.5rem' }}>
                <button type="button" className="button-secondary" onClick={() => ex.loadFromHistory(h)}>{t('explorer.load')}</button>
                <button type="button" className="icon-button" onClick={() => ex.deleteHistory(h.id)} aria-label={t('common.delete')}>✕</button>
              </span>
            </div>
          ))}
        </div>
      )}

      <div className="explorer-layout">
        <div className="explorer-op-select">
          <label htmlFor="explorer-operation">{t('explorer.operations')}</label>
          {ex.specLoading ? (
            <div className="table-loading"><div className="loading-spinner" /></div>
          ) : (
            <select
              id="explorer-operation"
              value={ex.operationId || ''}
              onChange={(e) => ex.selectOperation(e.target.value)}
            >
              <option value="" disabled>{t('explorer.selectOp')}</option>
              {ex.groups.map((g) => (
                <optgroup key={g.tag} label={g.tag}>
                  {g.operations.map((op) => (
                    <option key={op.id} value={op.id}>
                      {op.method} {op.path}{op.summary ? ` — ${op.summary}` : ''}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
          )}
        </div>

        <div className="explorer-panel">
          {!ex.operation ? (
            <p style={{ color: 'var(--color-text-secondary)' }}>{t('explorer.selectOp')}</p>
          ) : (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '1rem', flexWrap: 'wrap' }}>
                <span className={methodClass(ex.operation.method)}>{ex.operation.method}</span>
                <code style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>{ex.operation.path}</code>
              </div>
              {ex.operation.summary && <p style={{ color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)', marginBottom: '1rem' }}>{ex.operation.summary}</p>}

              {pathParamDefs.length > 0 && (
                <div className="explorer-field-group">
                  <h3>{t('explorer.pathParams')}</h3>
                  <ParamRows params={pathParamDefs} values={ex.pathParams} onChange={ex.setPathParams} />
                </div>
              )}
              {queryParamDefs.length > 0 && (
                <div className="explorer-field-group">
                  <h3>{t('explorer.queryParams')}</h3>
                  <ParamRows params={queryParamDefs} values={ex.queryParams} onChange={ex.setQueryParams} />
                </div>
              )}
              {pagingParamDefs.length > 0 && (
                <div className="explorer-field-group">
                  <h3>{t('explorer.pagination', 'Pagination')}</h3>
                  <ParamRows params={pagingParamDefs} values={ex.queryParams} onChange={ex.setQueryParams} />
                </div>
              )}
              {hasBody && (
                <div className="explorer-field-group">
                  <h3>{t('explorer.body')}</h3>
                  <textarea rows={10} value={ex.body} onChange={(e) => ex.setBody(e.target.value)} />
                  {jsonError && <div className="error-message" style={{ marginTop: '0.5rem' }}>{jsonError}</div>}
                </div>
              )}

              <div className="explorer-field-group">
                <h3>{t('explorer.resolvedUrl')}</h3>
                <div className="explorer-resolved-url">
                  <span style={{ flex: 1 }}>{ex.resolvedUrl}</span>
                  <CopyButton value={ex.resolvedUrl} />
                </div>
              </div>

              <button type="button" className="button-primary" onClick={validateAndRun} disabled={ex.running}>
                {ex.running ? t('explorer.running') : t('explorer.execute')}
              </button>

              {ex.response && (
                <div style={{ marginTop: '1.5rem' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '0.75rem', flexWrap: 'wrap' }}>
                    <StatusPill label={`${ex.response.status || 'ERR'} ${ex.response.statusText || ''}`} tone={ex.response.error ? 'danger' : toneForHttpStatus(ex.response.status)} />
                    <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-text-secondary)' }}>{formatDuration(ex.response.durationMs)} · {ex.response.bytes ?? 0} B</span>
                  </div>
                  <div className="explorer-tabs">
                    {['body', 'headers', 'code'].map((tb) => (
                      <button key={tb} type="button" className={`explorer-tab ${tab === tb ? 'active' : ''}`} onClick={() => setTab(tb)}>
                        {tb === 'body' ? t('explorer.responseBody') : tb === 'headers' ? t('explorer.responseHeaders') : 'Code'}
                      </button>
                    ))}
                  </div>
                  {tab === 'body' && (
                    <div>
                      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.5rem' }}><CopyButton value={ex.response.body} /></div>
                      <pre className="code-block">{(() => { try { return JSON.stringify(JSON.parse(ex.response.body), null, 2); } catch { return ex.response.body; } })()}</pre>
                    </div>
                  )}
                  {tab === 'headers' && (
                    <pre className="code-block">{Object.entries(ex.response.headers).map(([k, v]) => `${k}: ${v}`).join('\n') || '—'}</pre>
                  )}
                  {tab === 'code' && (
                    <div>
                      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.5rem' }}><CopyButton value={snippets.curl} label="curl" /></div>
                      <pre className="code-block">{snippets.curl}</pre>
                      <div style={{ display: 'flex', justifyContent: 'flex-end', margin: '0.5rem 0' }}><CopyButton value={snippets.fetch} label="fetch" /></div>
                      <pre className="code-block">{snippets.fetch}</pre>
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {confirm && ex.operation && (
        <ConfirmModal
          title={ex.operation.method}
          message={t('explorer.confirmDestructive', { method: ex.operation.method })}
          confirmLabel={t('explorer.execute')}
          onConfirm={() => { ex.execute(); }}
          onClose={() => setConfirm(false)}
        />
      )}
    </div>
  );
}

export default ApiExplorerView;
