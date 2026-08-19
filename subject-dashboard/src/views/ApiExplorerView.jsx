import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApiExplorer } from '../hooks/useApiExplorer';
import { groupOperationsByTag } from '../utils/openApi';
import { formatDurationMs, formatBytes } from '../utils/format';
import PageHeader from '../components/PageHeader';
import ConfirmModal from '../components/ConfirmModal';
import CopyButton from '../components/CopyButton';
import './ApiExplorer.css';

function KeyValueEditor({ label, values, onChange, keyPlaceholder = 'name', locked = false }) {
  const entries = Object.entries(values || {});
  return (
    <div className="kv-editor">
      <div className="kv-label">{label}</div>
      {entries.map(([key, val]) => (
        <div className="kv-row" key={key}>
          <input value={key} disabled={locked} readOnly={locked} placeholder={keyPlaceholder} />
          <input
            value={val}
            onChange={(e) => onChange({ ...values, [key]: e.target.value })}
            placeholder="value"
          />
        </div>
      ))}
      {entries.length === 0 && <div className="kv-empty">None</div>}
    </div>
  );
}

function ApiExplorerView() {
  const { t } = useTranslation();
  const { apiClient, serverUrl } = useAuth();
  const explorer = useApiExplorer(apiClient, serverUrl);
  const [search, setSearch] = useState('');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [customHeaderKey, setCustomHeaderKey] = useState('');

  const {
    operations,
    operation,
    setOperationId,
    pathParams,
    setPathParams,
    queryParams,
    setQueryParams,
    headers,
    setHeaders,
    body,
    setBody,
    resolvedUrl,
    snippets,
    isDestructive,
    response,
    executing,
    execute,
    specLoading,
    specError,
    history,
    loadFromHistory,
    deleteHistoryItem
  } = explorer;

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return operations;
    return operations.filter(
      (op) =>
        op.path.toLowerCase().includes(q) ||
        op.summary.toLowerCase().includes(q) ||
        op.id.toLowerCase().includes(q) ||
        op.method.toLowerCase().includes(q)
    );
  }, [operations, search]);

  const groups = useMemo(() => groupOperationsByTag(filtered), [filtered]);

  const handleExecuteClick = () => {
    if (isDestructive) setConfirmOpen(true);
    else execute();
  };
  const confirmExecute = () => {
    setConfirmOpen(false);
    execute();
  };

  const addHeader = () => {
    const key = customHeaderKey.trim();
    if (key) {
      setHeaders({ ...headers, [key]: '' });
      setCustomHeaderKey('');
    }
  };

  if (specLoading) {
    return (
      <div>
        <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />
        <div className="loading-block"><span className="loading-spinner" /> {t('common.loading')}</div>
      </div>
    );
  }

  if (specError) {
    return (
      <div>
        <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />
        <div className="empty-state">
          <div className="empty-state-title">{t('explorer.noSpec')}</div>
          <div className="empty-state-description">{specError}</div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />

      <div className="explorer-layout">
        <aside className="explorer-sidebar card">
          <div className="explorer-search">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('common.search')}
            />
          </div>
          <div className="explorer-op-list">
            {groups.map((group) => (
              <div className="explorer-group" key={group.tag}>
                <div className="explorer-group-title">{group.tag}</div>
                {group.ops.map((op) => (
                  <button
                    key={op.id}
                    className={`explorer-op ${operation?.id === op.id ? 'active' : ''}`}
                    onClick={() => setOperationId(op.id)}
                    title={op.summary}
                  >
                    <span className={`method-pill method-${op.method.toLowerCase()}`}>{op.method}</span>
                    <span className="explorer-op-path">{op.path}</span>
                  </button>
                ))}
              </div>
            ))}
            {history.length > 0 && (
              <div className="explorer-group">
                <div className="explorer-group-title">Recent</div>
                {history.map((h) => (
                  <div className="explorer-history-row" key={h.id}>
                    <button className="explorer-op" onClick={() => loadFromHistory(h)} title={h.path}>
                      <span className={`method-pill method-${h.method.toLowerCase()}`}>{h.method}</span>
                      <span className="explorer-op-path">{h.path}</span>
                    </button>
                    <button className="btn-icon" onClick={() => deleteHistoryItem(h.id)} title={t('common.delete')}>
                      &times;
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </aside>

        <section className="explorer-main card">
          {!operation ? (
            <div className="empty-state">
              <div className="empty-state-description">Select an operation to build a request.</div>
            </div>
          ) : (
            <div className="explorer-request">
              <div className="explorer-op-header">
                <span className={`method-pill method-${operation.method.toLowerCase()}`}>{operation.method}</span>
                <code className="explorer-op-summary">{operation.summary}</code>
              </div>

              <div className="explorer-url-bar">
                <code>{resolvedUrl}</code>
                <CopyButton value={resolvedUrl} title={t('explorer.resolvedUrl')} />
              </div>

              {Object.keys(pathParams).length > 0 && (
                <KeyValueEditor label={t('explorer.pathParams')} values={pathParams} onChange={setPathParams} locked />
              )}

              {Object.keys(queryParams).length > 0 && (
                <KeyValueEditor label={t('explorer.queryParams')} values={queryParams} onChange={setQueryParams} />
              )}

              <div className="kv-editor">
                <div className="kv-label">{t('explorer.headers')}</div>
                {Object.entries(headers).map(([key, val]) => (
                  <div className="kv-row" key={key}>
                    <input value={key} disabled readOnly />
                    <input value={val} onChange={(e) => setHeaders({ ...headers, [key]: e.target.value })} placeholder="value" />
                    <button className="btn-icon" onClick={() => {
                      const next = { ...headers };
                      delete next[key];
                      setHeaders(next);
                    }} title={t('common.delete')}>&times;</button>
                  </div>
                ))}
                <div className="kv-row">
                  <input value={customHeaderKey} onChange={(e) => setCustomHeaderKey(e.target.value)} placeholder="Header name" />
                  <button className="btn btn-secondary btn-sm" onClick={addHeader}>Add</button>
                </div>
              </div>

              {operation.method !== 'GET' && operation.method !== 'HEAD' && (
                <div className="explorer-body">
                  <div className="kv-label">{t('explorer.body')}</div>
                  <textarea
                    className="explorer-body-editor"
                    rows={8}
                    value={body}
                    onChange={(e) => setBody(e.target.value)}
                    spellCheck={false}
                  />
                </div>
              )}

              <div className="explorer-actions">
                <button className="btn btn-primary" onClick={handleExecuteClick} disabled={executing}>
                  {executing ? t('explorer.executing') : t('explorer.execute')}
                </button>
              </div>

              {response && (
                <div className="explorer-response">
                  <div className="kv-label">{t('explorer.response')}</div>
                  {response.error ? (
                    <div className="form-error">{response.error}</div>
                  ) : (
                    <>
                      <div className="explorer-response-meta">
                        <span className={`status-pill ${response.status < 400 ? 'pill-success' : 'pill-danger'}`}>
                          {response.status} {response.statusText}
                        </span>
                        <span>{formatDurationMs(response.durationMs)}</span>
                        <span>{formatBytes(response.sizeBytes)}</span>
                      </div>
                      <div className="explorer-code-block">
                        <CopyButton value={response.body} title="Copy response" className="explorer-copy-float" />
                        <pre>{response.body || '(empty)'}</pre>
                      </div>
                    </>
                  )}
                </div>
              )}

              <div className="explorer-snippets">
                <div className="kv-label">{t('explorer.snippets')}</div>
                <div className="explorer-code-block">
                  <CopyButton value={snippets.curl} title="Copy curl" className="explorer-copy-float" />
                  <pre>{snippets.curl}</pre>
                </div>
                <div className="explorer-code-block">
                  <CopyButton value={snippets.fetch} title="Copy fetch" className="explorer-copy-float" />
                  <pre>{snippets.fetch}</pre>
                </div>
              </div>
            </div>
          )}
        </section>
      </div>

      <ConfirmModal
        isOpen={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        onConfirm={confirmExecute}
        title={t('explorer.confirmTitle')}
        message={t('explorer.confirmMessage')}
        entityName={operation ? `${operation.method} ${operation.path}` : ''}
        confirmLabel={t('explorer.execute')}
      />
    </div>
  );
}

export default ApiExplorerView;
