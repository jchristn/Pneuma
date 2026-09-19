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

// Masked reveal for a stored secret in the read-only detail view. The single-endpoint GET returns the
// decrypted API key so an operator can inspect it here (a deliberate product decision); it stays masked
// until revealed and is never shown in the list.
function SecretReveal({ value }) {
  const { t } = useTranslation();
  const [show, setShow] = useState(false);
  if (!value) return <span>—</span>;
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}>
      <code className="wrap">{show ? value : '•'.repeat(Math.min(24, value.length))}</code>
      <button type="button" className="button-secondary button-small" onClick={() => setShow((s) => !s)}>
        {show ? t('common.hide') : t('common.show')}
      </button>
    </span>
  );
}

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
  const [notice, setNotice] = useState('');
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

  // "Pending" health: health checks are enabled for the endpoint (and it's active), but no probe has run
  // yet — the same state renderHealth surfaces as "Pending". This mirrors renderHealth's `checked` test.
  const isHealthPending = useCallback((row) => {
    if (row.healthCheckEnabled !== true) return false;
    if (row.active === false) return false;
    const h = health[row.id];
    const checked = h && (h.lastCheckUtc || h.LastCheckUtc);
    return !checked;
  }, [health]);

  // Run a single health probe on demand and refresh the endpoint's health so the badge updates. Handles the
  // backend's 400 (health checks disabled) and 404 (not found) with the app's notice pattern.
  const runHealthCheck = useCallback(async (row) => {
    const label = row.name || row.model || row.id;
    try {
      const dto = await apiClient.runModelEndpointHealthCheck(row.id);
      if (mounted.current && dto) {
        const id = dto.endpointId || dto.EndpointId || row.id;
        setHealth((prev) => ({ ...prev, [id]: dto }));
      }
      if (mounted.current) setNotice(t('modelRunners.healthcheckSuccess', { name: label }));
      loadHealth();
    } catch (err) {
      if (!mounted.current) return;
      if (err?.status === 400) setNotice(t('modelRunners.healthcheckDisabled'));
      else if (err?.status === 404) setNotice(t('modelRunners.healthcheckNotFound'));
      else setNotice(`${t('modelRunners.healthcheckError')} ${err?.message || ''}`.trim());
    }
  }, [apiClient, loadHealth, t]);

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
    {
      name: 'apiKey', label: t('modelRunners.apiKey'), type: 'password', visibleWhen: (v) => !isBedrock(v),
      editPlaceholder: t('modelRunners.secretUnchanged'), tip: t('modelRunners.apiKeyTip'),
      hint: (v) => {
        if (v.provider === 'Gemini') return t('modelRunners.apiKeyHintGemini');
        if (v.provider === 'Anthropic') return t('modelRunners.apiKeyHintAnthropic');
        if (v.provider === 'VertexAI') return t('modelRunners.apiKeyHintVertex');
        return '';
      }
    },

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
  // The detail view fetches the single endpoint (which returns the decrypted key), so surface the API key
  // here as a masked reveal; other write-only secrets stay out of the view.
  const detailFields = [
    ...formFields.filter((f) => f.type !== 'section' && !SECRET_FIELDS.includes(f.name)),
    // The single-endpoint read returns the stored secret in the provider-appropriate field: the AWS secret
    // access key for Bedrock, the API key for every other provider. Reveal whichever applies.
    { name: 'credential', label: t('modelRunners.credential'), render: (r) => (isBedrock(r) ? <SecretReveal value={r.secretAccessKey} /> : <SecretReveal value={r.apiKey} />) }
  ];

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
        fetchDetail
        duplicable
        duplicateTransform={(r) => ({ ...r, name: r.name ? `${r.name} (copy)` : '' })}
        extraActions={[
          { key: 'startHealthcheck', label: t('modelRunners.startHealthcheck'), tip: t('modelRunners.startHealthcheckTip'), hidden: (row) => !isHealthPending(row), onClick: runHealthCheck },
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
      {notice && (
        <Modal
          title={t('common.notice')}
          size="sm"
          onClose={() => setNotice('')}
          footer={<button type="button" className="button-primary" onClick={() => setNotice('')}>{t('common.close')}</button>}
        >
          <p className="confirm-text">{notice}</p>
        </Modal>
      )}
    </>
  );
}

export default ModelRunnersView;
