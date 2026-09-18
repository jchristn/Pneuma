import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import ResourceView from '../components/ResourceView';
import StatusPill from '../components/StatusPill';
import Modal from '../components/Modal';
import { HealthHistogram, HealthDetailModal } from '../components/HealthHistogram';

const TYPES = ['Embedding', 'Completion'];

// All providers Pneuma can drive natively. Values match the backend ModelEndpoint `provider` enum;
// labels are provider brand names (not translated).
const PROVIDERS = [
  'OpenAI', 'OpenAICompatible', 'Gemini', 'Ollama', 'AzureOpenAI', 'Anthropic', 'Bedrock', 'VoyageAI', 'VertexAI'
];
const PROVIDER_LABELS = {
  OpenAI: 'OpenAI',
  OpenAICompatible: 'OpenAI-Compatible',
  Gemini: 'Google Gemini',
  Ollama: 'Ollama',
  AzureOpenAI: 'Azure OpenAI',
  Anthropic: 'Anthropic',
  Bedrock: 'AWS Bedrock',
  VoyageAI: 'Voyage AI',
  VertexAI: 'Google Vertex AI'
};

// Anthropic exposes no embeddings API; Voyage AI exposes no completion API. Constrain the picker so an
// invalid provider/type pairing can't be created.
function providersForType(type) {
  return PROVIDERS.filter((p) => {
    if (type === 'Embedding' && p === 'Anthropic') return false;
    if (type === 'Completion' && p === 'VoyageAI') return false;
    return true;
  });
}

const isAzure = (v) => v.provider === 'AzureOpenAI';
const isBedrock = (v) => v.provider === 'Bedrock';
const isVertex = (v) => v.provider === 'VertexAI';
const hasProviderExtras = (v) => isAzure(v) || isBedrock(v) || isVertex(v);

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
            <div style={{ display: 'contents' }}><dt>{t('modelRunners.provider')}</dt><dd>{(data.provider && (PROVIDER_LABELS[data.provider] || data.provider)) || '—'}</dd></div>
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
                  <td>
                    <StatusPill
                      label={c.warning ? 'N/A' : (c.ok ? 'Pass' : 'Fail')}
                      tone={c.warning ? 'neutral' : (c.ok ? 'success' : 'danger')}
                    />
                  </td>
                  <td className="wrap">{c.warning ? (c.detail || c.error || '—') : (c.ok ? (c.detail || '—') : (c.error || '—'))}</td>
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
    setValidation({ id: row.id, type: row.type, name: label, loading: true, data: null, error: null });
    try {
      const data = await apiClient.validateModelRunner(row.id, row.type);
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
    { key: 'name', label: t('modelRunners.name'), render: (r) => r.name || '—' },
    { key: 'model', label: t('modelRunners.model'), render: (r) => r.model || '—' },
    { key: 'endpoint', label: t('modelRunners.endpoint'), cellClass: 'wrap', sortable: false, render: (r) => r.endpoint || '—' },
    { key: 'provider', label: t('modelRunners.provider'), render: (r) => (r.provider && (PROVIDER_LABELS[r.provider] || r.provider)) || '—' },
    { key: 'health', label: t('modelRunners.health'), sortable: false, render: renderHealth, tip: t('modelRunners.healthTip') },
    { key: 'active', label: t('modelRunners.active'), tip: t('modelRunners.activeColTip'), render: (r) => <StatusPill label={r.active === false ? 'Disabled' : 'Active'} tone={r.active === false ? 'neutral' : 'success'} /> }
  ];

  const formFields = [
    { name: '__sec_basic', type: 'section', label: t('modelRunners.sectionBasic') },
    { name: 'type', label: t('modelRunners.type'), type: 'select', required: true, default: 'Embedding', options: TYPES.map((x) => ({ value: x, label: x })), tip: t('modelRunners.typeTip') },
    { name: 'name', label: t('modelRunners.name'), tip: t('modelRunners.nameTip') },
    {
      name: 'provider', label: t('modelRunners.provider'), type: 'select', required: true, default: 'OpenAI',
      options: (v) => providersForType(v.type).map((p) => ({ value: p, label: PROVIDER_LABELS[p] })),
      hint: (v) => (v.type === 'Embedding' ? t('modelRunners.providerHelpEmbedding') : t('modelRunners.providerHelpCompletion')),
      tip: t('modelRunners.providerTip'),
      render: (r) => (r.provider && (PROVIDER_LABELS[r.provider] || r.provider)) || '—'
    },
    { name: 'model', label: t('modelRunners.model'), required: true, placeholder: 'nomic-embed-text', tip: t('modelRunners.modelTip') },
    { name: 'endpoint', label: t('modelRunners.endpoint'), required: true, placeholder: 'https://api.openai.com', tip: t('modelRunners.endpointTip') },
    { name: 'apiKey', label: t('modelRunners.apiKey'), type: 'password', editPlaceholder: t('modelRunners.secretUnchanged'), tip: t('modelRunners.apiKeyTip') },

    // Provider-specific settings. Shown only for the providers that require them.
    { name: '__sec_provider', type: 'section', label: t('modelRunners.sectionProvider'), visibleWhen: hasProviderExtras },
    { name: 'deployment', label: t('modelRunners.deployment'), visibleWhen: isAzure, placeholder: 'my-gpt4o-deployment', tip: t('modelRunners.deploymentTip') },
    { name: 'apiVersion', label: t('modelRunners.apiVersion'), visibleWhen: isAzure, placeholder: '2024-02-01', tip: t('modelRunners.apiVersionTip') },
    { name: 'region', label: t('modelRunners.region'), visibleWhen: (v) => isBedrock(v) || isVertex(v), placeholder: 'us-east-1', tip: t('modelRunners.regionTip') },
    { name: 'project', label: t('modelRunners.project'), visibleWhen: isVertex, placeholder: 'my-gcp-project', tip: t('modelRunners.projectTip') },
    { name: 'accessKeyId', label: t('modelRunners.accessKeyId'), visibleWhen: isBedrock, tip: t('modelRunners.accessKeyIdTip') },
    { name: 'secretAccessKey', label: t('modelRunners.secretAccessKey'), type: 'password', visibleWhen: isBedrock, editPlaceholder: t('modelRunners.secretUnchanged'), tip: t('modelRunners.secretAccessKeyTip') },
    { name: 'sessionToken', label: t('modelRunners.sessionToken'), type: 'password', visibleWhen: isBedrock, editPlaceholder: t('modelRunners.secretUnchanged'), tip: t('modelRunners.sessionTokenTip') },

    { name: '__sec_request', type: 'section', label: t('modelRunners.sectionRequest') },
    { name: 'active', label: t('modelRunners.active'), type: 'checkbox', default: true, omitIfEmpty: false, tip: t('modelRunners.activeTip') },
    { name: 'maxConcurrentRequests', label: t('modelRunners.maxConcurrency'), type: 'number', default: 2, min: 1, placeholder: '2', tip: t('modelRunners.maxConcurrencyTip') },
    { name: 'maxQueueDepth', label: t('modelRunners.maxQueueDepth'), type: 'number', default: 0, min: 0, placeholder: '0', tip: t('modelRunners.maxQueueDepthTip') },
    { name: 'maximumTimeoutMs', label: t('modelRunners.requestTimeout'), type: 'number', default: 60000, min: 1000, step: 1000, placeholder: '60000', tip: t('modelRunners.requestTimeoutTip') },
    { name: 'contextSize', label: t('modelRunners.contextSize'), type: 'number', default: 0, min: 0, placeholder: '8192', tip: t('modelRunners.contextSizeTip') },

    { name: '__sec_health', type: 'section', label: t('modelRunners.sectionHealth') },
    { name: 'healthCheckEnabled', label: t('modelRunners.healthCheckEnabled'), type: 'checkbox', default: true, omitIfEmpty: false, tip: t('modelRunners.healthCheckEnabledTip') },
    { name: 'healthCheckUrl', label: t('modelRunners.healthCheckUrl'), placeholder: t('modelRunners.healthCheckUrlPlaceholder'), tip: t('modelRunners.healthCheckUrlTip') },
    { name: 'healthCheckMethod', label: t('modelRunners.healthCheckMethod'), type: 'select', default: 'GET', options: ['GET', 'HEAD'].map((x) => ({ value: x, label: x })), tip: t('modelRunners.healthCheckMethodTip') },
    { name: 'healthCheckIntervalMs', label: t('modelRunners.healthCheckInterval'), type: 'number', default: 30000, min: 1000, step: 1000, placeholder: '30000', tip: t('modelRunners.healthCheckIntervalTip') },
    { name: 'healthCheckTimeoutMs', label: t('modelRunners.healthCheckTimeout'), type: 'number', default: 5000, min: 100, step: 100, placeholder: '5000', tip: t('modelRunners.healthCheckTimeoutTip') },
    { name: 'healthCheckExpectedStatusCode', label: t('modelRunners.healthCheckExpectedStatus'), type: 'number', default: 200, min: 100, placeholder: '200', tip: t('modelRunners.healthCheckExpectedStatusTip') },
    { name: 'healthyThreshold', label: t('modelRunners.healthyThreshold'), type: 'number', default: 2, min: 1, placeholder: '2', tip: t('modelRunners.healthyThresholdTip') },
    { name: 'unhealthyThreshold', label: t('modelRunners.unhealthyThreshold'), type: 'number', default: 2, min: 1, placeholder: '2', tip: t('modelRunners.unhealthyThresholdTip') },
    { name: 'healthCheckUseAuth', label: t('modelRunners.healthCheckUseAuth'), type: 'checkbox', default: false, omitIfEmpty: false, tip: t('modelRunners.healthCheckUseAuthTip') }
  ];

  const SECRET_FIELDS = ['apiKey', 'secretAccessKey', 'sessionToken'];
  const detailFields = formFields.filter((f) => f.type !== 'section' && !SECRET_FIELDS.includes(f.name));

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
          const row = { id: validation.id, type: validation.type, name: validation.name };
          runValidation(row);
        }} />
      )}
    </>
  );
}

export default ModelRunnersView;
