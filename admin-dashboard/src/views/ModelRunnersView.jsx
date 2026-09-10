import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import ResourceView from '../components/ResourceView';
import StatusPill from '../components/StatusPill';
import Modal from '../components/Modal';
import { HealthHistogram, HealthDetailModal } from '../components/HealthHistogram';

const TYPES = ['Embedding', 'Completion'];
const API_FORMATS = ['Ollama', 'OpenAI', 'Gemini'];
const HEALTH_POLL_MS = 15000;

function typeTone(type) {
  if (type === 'Embedding') return 'info';
  if (type === 'Completion') return 'warning';
  return 'neutral';
}

function ValidationModal({ state, onClose, onRetry }) {
  const { t } = useTranslation();
  const { loading, data, error, name } = state;
  const checks = Array.isArray(data?.checks) ? data.checks : [];

  const footer = (
    <>
      <button type="button" className="button-secondary" onClick={onRetry} disabled={loading}>
        {loading ? t('modelRunners.validateRunning') : t('common.retry', 'Retry')}
      </button>
      <button type="button" className="button-primary" onClick={onClose}>{t('common.close')}</button>
    </>
  );

  return (
    <Modal title={t('modelRunners.validateTitle', { name })} size="lg" onClose={onClose} footer={footer}>
      {loading && <p className="confirm-text">{t('modelRunners.validateRunning')}</p>}
      {!loading && error && (
        <div className="error-message" style={{ marginBottom: '1rem' }}>
          {t('modelRunners.validateError')}: {error}
        </div>
      )}
      {!loading && data && (
        <>
          <div style={{ marginBottom: '1rem' }}>
            <StatusPill
              label={data.ok ? t('modelRunners.validatePassed') : t('modelRunners.validateFailed')}
              tone={data.ok ? 'success' : 'danger'}
            />
          </div>
          <dl className="kv-grid" style={{ marginBottom: '1rem' }}>
            <div style={{ display: 'contents' }}><dt>{t('modelRunners.type')}</dt><dd>{data.type || '—'}</dd></div>
            <div style={{ display: 'contents' }}><dt>{t('modelRunners.model')}</dt><dd>{data.model || '—'}</dd></div>
            <div style={{ display: 'contents' }}><dt>{t('modelRunners.endpoint')}</dt><dd className="wrap">{data.endpoint || '—'}</dd></div>
            <div style={{ display: 'contents' }}><dt>{t('modelRunners.apiFormat')}</dt><dd>{data.apiFormat || '—'}</dd></div>
          </dl>
          <table className="data-table">
            <thead>
              <tr>
                <th>{t('modelRunners.validateCheck')}</th>
                <th>{t('modelRunners.validateResult')}</th>
                <th>{t('modelRunners.validateDetail')}</th>
                <th style={{ textAlign: 'right' }}>{t('modelRunners.validateDuration')}</th>
              </tr>
            </thead>
            <tbody>
              {checks.map((c, i) => (
                <tr key={i}>
                  <td>{c.name}</td>
                  <td><StatusPill label={c.ok ? 'Pass' : 'Fail'} tone={c.ok ? 'success' : 'danger'} /></td>
                  <td className="wrap">{c.ok ? (c.detail || '—') : (c.error || '—')}</td>
                  <td style={{ textAlign: 'right' }}>{c.durationMs != null ? `${Math.round(c.durationMs)} ms` : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </Modal>
  );
}

function ModelRunnersView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [health, setHealth] = useState({});
  const [healthModal, setHealthModal] = useState(null);
  const [validation, setValidation] = useState(null);
  const mounted = useRef(true);

  const loadHealth = useCallback(async () => {
    try {
      const list = await apiClient.getModelRunnerHealth();
      const arr = Array.isArray(list) ? list : (list?.objects || list?.Objects || []);
      const map = {};
      for (const h of arr) {
        const id = h.endpointId || h.EndpointId;
        if (id) map[id] = h;
      }
      if (mounted.current) setHealth(map);
    } catch {
      /* health is best-effort; leave the last known map in place */
    }
  }, [apiClient]);

  useEffect(() => {
    mounted.current = true;
    loadHealth();
    const timer = setInterval(loadHealth, HEALTH_POLL_MS);
    return () => { mounted.current = false; clearInterval(timer); };
  }, [loadHealth]);

  const openHealthDetail = useCallback(async (row) => {
    setHealthModal({ id: row.id, title: `${row.name || row.model || row.id} — Health`, data: health[row.id] || null, loading: true });
    try {
      const fresh = await apiClient.getModelRunnerHealthById(row.id);
      if (mounted.current) setHealthModal((m) => (m && m.id === row.id ? { ...m, data: fresh, loading: false } : m));
    } catch {
      if (mounted.current) setHealthModal((m) => (m && m.id === row.id ? { ...m, loading: false } : m));
    }
  }, [apiClient, health]);

  const runValidation = useCallback(async (row) => {
    const label = row.name || row.model || row.id;
    setValidation({ id: row.id, name: label, loading: true, data: null, error: null });
    try {
      const data = await apiClient.validateModelRunner(row.id);
      if (mounted.current) setValidation((v) => (v && v.id === row.id ? { ...v, loading: false, data } : v));
    } catch (err) {
      if (mounted.current) setValidation((v) => (v && v.id === row.id ? { ...v, loading: false, error: err?.message || 'Validation failed' } : v));
    }
  }, [apiClient]);

  const renderHealth = (row) => {
    if (row.active === false) return <StatusPill label="Inactive" tone="neutral" />;
    const h = health[row.id];
    const checked = h && (h.lastCheckUtc || h.LastCheckUtc);
    if (!checked) return <span style={{ color: 'var(--color-text-secondary)' }}>{t('modelRunners.healthPending')}</span>;
    const healthy = (h.isHealthy ?? h.IsHealthy) === true;
    return (
      <span
        className="health-cell"
        role="button"
        tabIndex={0}
        data-row-click-ignore="true"
        title={t('modelRunners.healthDetailHint')}
        onClick={(e) => { e.stopPropagation(); openHealthDetail(row); }}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openHealthDetail(row); } }}
      >
        <StatusPill label={healthy ? 'Healthy' : 'Unhealthy'} tone={healthy ? 'success' : 'danger'} />
        <HealthHistogram history={h.history || h.History || []} maxBars={10} />
      </span>
    );
  };

  const columns = [
    { key: 'type', label: t('modelRunners.type'), render: (r) => <StatusPill label={r.type} tone={typeTone(r.type)} /> },
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'model', label: t('modelRunners.model'), render: (r) => r.model || '—' },
    { key: 'endpoint', label: t('modelRunners.endpoint'), cellClass: 'wrap', sortable: false, render: (r) => r.endpoint || '—' },
    { key: 'apiFormat', label: t('modelRunners.apiFormat'), render: (r) => r.apiFormat || '—' },
    { key: 'health', label: t('modelRunners.health'), sortable: false, render: renderHealth, tip: 'Live reachability of the endpoint, polled periodically. Click a health cell for recent history.' },
    { key: 'active', label: 'Active', tip: 'Whether this endpoint is currently in use.', render: (r) => <StatusPill label={r.active === false ? 'Disabled' : 'Active'} tone={r.active === false ? 'neutral' : 'success'} /> }
  ];

  const formFields = [
    { name: 'type', label: t('modelRunners.type'), type: 'select', required: true, default: 'Embedding', options: TYPES.map((x) => ({ value: x, label: x })), tip: 'Embedding endpoints turn text into vectors for search; Completion endpoints generate answers. Pick which role this model serves.' },
    { name: 'name', label: 'Name', tip: 'A human-readable label for this endpoint (e.g. "local-embed"). Does not affect behavior.' },
    { name: 'model', label: t('modelRunners.model'), required: true, placeholder: 'nomic-embed-text', tip: "The provider's model identifier exactly as it expects it — e.g. 'nomic-embed-text' for embeddings or 'llama3.2' for chat." },
    { name: 'endpoint', label: t('modelRunners.endpoint'), required: true, placeholder: 'http://ollama:11434', tip: 'Base URL of the provider serving this model. For the bundled Ollama use http://ollama:11434.' },
    { name: 'apiFormat', label: t('modelRunners.apiFormat'), type: 'select', default: 'Ollama', options: API_FORMATS.map((x) => ({ value: x, label: x })), tip: 'The wire protocol this endpoint speaks. Ollama for the local runner; OpenAI/Gemini for those hosted APIs.' },
    { name: 'apiKey', label: 'API Key (write-only)', type: 'password', tip: 'Secret key for hosted providers. Stored encrypted and never returned. Leave blank for a keyless local Ollama.' },
    { name: 'maxConcurrentRequests', label: t('modelRunners.maxConcurrency'), type: 'number', default: 1, min: 1, placeholder: '1', tip: 'Cap on simultaneous requests Partio opens to this endpoint. Keep at 1 for a single local model so each inference runs unshared and avoids upstream timeouts; raise for scaled hosted APIs.' },
    { name: 'maxQueueDepth', label: t('modelRunners.maxQueueDepth'), type: 'number', default: 0, min: 0, placeholder: '0', tip: 'How many requests may wait for a slot once the concurrency cap is hit. 0 rejects over-limit requests immediately (429); raise it to let ingestion bursts queue instead of bouncing. A queued request that waits past the endpoint timeout returns 504.' },
    { name: 'contextSize', label: t('modelRunners.contextSize'), type: 'number', default: 0, min: 0, placeholder: '8192', tip: 'Completion models only: the model’s context window in tokens (e.g. 8192). When the chat history approaches this, the conversation is automatically compacted into a summary. 0 disables compaction.' },
    { name: 'active', label: 'Active', type: 'checkbox', default: true, omitIfEmpty: false, tip: 'When off, this endpoint is kept but not used for ingestion or answering.' }
  ];

  const detailFields = formFields.filter((f) => f.name !== 'apiKey');

  return (
    <>
      <ResourceView
        resourceKey="model-runners"
        singular="model endpoint"
        title={t('modelRunners.title')}
        subtitle={t('modelRunners.subtitle')}
        columns={columns}
        formFields={formFields}
        detailFields={detailFields}
        idField="id"
        duplicable
        duplicateTransform={(r) => ({ ...r, name: r.name ? `${r.name} (copy)` : '' })}
        extraActions={[
          { key: 'validate', label: t('modelRunners.validate'), tip: t('modelRunners.validateTip'), onClick: runValidation }
        ]}
      />
      {healthModal && (
        <HealthDetailModal title={healthModal.title} health={healthModal.data} loading={healthModal.loading}
          onClose={() => setHealthModal(null)} />
      )}
      {validation && (
        <ValidationModal state={validation} onClose={() => setValidation(null)} onRetry={() => {
          const row = { id: validation.id, name: validation.name };
          runValidation(row);
        }} />
      )}
    </>
  );
}

export default ModelRunnersView;
