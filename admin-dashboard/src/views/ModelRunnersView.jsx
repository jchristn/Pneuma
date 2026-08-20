import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import { HealthHistogram, HealthDetailModal } from '../components/HealthHistogram';

const TYPES = ['Embedding', 'Completion'];
const API_FORMATS = ['Ollama', 'OpenAI', 'Gemini'];
const HEALTH_POLL_MS = 15000;

function typeTone(type) {
  if (type === 'Embedding') return 'info';
  if (type === 'Completion') return 'warning';
  return 'neutral';
}

function ModelRunnersView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [health, setHealth] = useState({});
  const [healthModal, setHealthModal] = useState(null);
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
    { key: 'active', label: 'Active', tip: 'Whether this endpoint is currently in use.', render: (r) => <StatusPill label={r.active === false ? 'Disabled' : 'Active'} tone={r.active === false ? 'neutral' : 'success'} /> },
    { key: 'maxConcurrentRequests', label: t('modelRunners.maxConcurrency'), tip: 'Max simultaneous requests Partio sends to this endpoint.', render: (r) => (r.maxConcurrentRequests ?? 1) },
    { key: 'contextSize', label: t('modelRunners.contextSize'), tip: 'Completion context window (tokens); drives automatic chat compaction. 0 = off.', render: (r) => (r.contextSize ? r.contextSize : '—') },
    { key: 'id', label: 'ID', tip: 'The Partio endpoint id. Click to copy — used when submitting links and in the API.', render: (r) => <CopyableId value={r.id} truncateLen={12} /> }
  ];

  const formFields = [
    { name: 'type', label: t('modelRunners.type'), type: 'select', required: true, default: 'Embedding', options: TYPES.map((x) => ({ value: x, label: x })), tip: 'Embedding endpoints turn text into vectors for search; Completion endpoints generate answers. Pick which role this model serves.' },
    { name: 'name', label: 'Name', tip: 'A human-readable label for this endpoint (e.g. "local-embed"). Does not affect behavior.' },
    { name: 'model', label: t('modelRunners.model'), required: true, placeholder: 'nomic-embed-text', tip: "The provider's model identifier exactly as it expects it — e.g. 'nomic-embed-text' for embeddings or 'llama3.2' for chat." },
    { name: 'endpoint', label: t('modelRunners.endpoint'), required: true, placeholder: 'http://ollama:11434', tip: 'Base URL of the provider serving this model. For the bundled Ollama use http://ollama:11434.' },
    { name: 'apiFormat', label: t('modelRunners.apiFormat'), type: 'select', default: 'Ollama', options: API_FORMATS.map((x) => ({ value: x, label: x })), tip: 'The wire protocol this endpoint speaks. Ollama for the local runner; OpenAI/Gemini for those hosted APIs.' },
    { name: 'apiKey', label: 'API Key (write-only)', type: 'password', tip: 'Secret key for hosted providers. Stored encrypted and never returned. Leave blank for a keyless local Ollama.' },
    { name: 'maxConcurrentRequests', label: t('modelRunners.maxConcurrency'), type: 'number', default: 1, min: 1, placeholder: '1', tip: 'Cap on simultaneous requests Partio opens to this endpoint. Keep at 1 for a single local model so each inference runs unshared and avoids upstream timeouts; raise for scaled hosted APIs.' },
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
      />
      {healthModal && (
        <HealthDetailModal title={healthModal.title} health={healthModal.data} loading={healthModal.loading}
          onClose={() => setHealthModal(null)} />
      )}
    </>
  );
}

export default ModelRunnersView;
