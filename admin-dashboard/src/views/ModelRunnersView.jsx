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
        <HealthHistogram history={h.history || h.History || []} />
      </span>
    );
  };

  const columns = [
    { key: 'type', label: t('modelRunners.type'), render: (r) => <StatusPill label={r.type} tone={typeTone(r.type)} /> },
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'model', label: t('modelRunners.model'), render: (r) => r.model || '—' },
    { key: 'endpoint', label: t('modelRunners.endpoint'), cellClass: 'wrap', sortable: false, render: (r) => r.endpoint || '—' },
    { key: 'apiFormat', label: t('modelRunners.apiFormat'), render: (r) => r.apiFormat || '—' },
    { key: 'health', label: t('modelRunners.health'), sortable: false, render: renderHealth },
    { key: 'active', label: 'Active', render: (r) => <StatusPill label={r.active === false ? 'Disabled' : 'Active'} tone={r.active === false ? 'neutral' : 'success'} /> },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id} truncateLen={12} /> }
  ];

  const formFields = [
    { name: 'type', label: t('modelRunners.type'), type: 'select', required: true, default: 'Embedding', options: TYPES.map((x) => ({ value: x, label: x })) },
    { name: 'name', label: 'Name' },
    { name: 'model', label: t('modelRunners.model'), required: true, placeholder: 'nomic-embed-text' },
    { name: 'endpoint', label: t('modelRunners.endpoint'), required: true, placeholder: 'http://ollama:11434' },
    { name: 'apiFormat', label: t('modelRunners.apiFormat'), type: 'select', default: 'Ollama', options: API_FORMATS.map((x) => ({ value: x, label: x })) },
    { name: 'apiKey', label: 'API Key (write-only)', type: 'password' },
    { name: 'active', label: 'Active', type: 'checkbox', default: true, omitIfEmpty: false }
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
