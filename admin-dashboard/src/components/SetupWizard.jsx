import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import Modal from './Modal';
import IngestionTimeline, { isTerminal } from './IngestionTimeline';
import './SetupWizard.css';

const API_FORMATS = ['Ollama', 'OpenAI', 'Gemini'];

const STEPS = [
  { key: 'models', title: 'Model endpoints', hint: 'Point Pneuma at the models that will embed and answer over your content.' },
  { key: 'subject', title: 'First subject', hint: 'A subject is the top-level thing an archive is about — a person, product, topic, or project.' },
  { key: 'link', title: 'First source', hint: 'Add a URL to ingest. Pneuma will crawl it, build the knowledge graph, and index it for retrieval.' },
  { key: 'ingest', title: 'Ingestion', hint: 'Watch the pipeline process your source end to end.' }
];

/** A labelled input with its own descriptive tooltip. */
function Field({ label, tip, children }) {
  return (
    <label className="wiz-field" title={tip}>
      <span className="wiz-field-label" title={tip}>{label}</span>
      {children}
    </label>
  );
}

/** One model-endpoint sub-form (embedding or completion). */
function EndpointForm({ kind, value, onChange }) {
  const set = (patch) => onChange({ ...value, ...patch });
  const isEmbed = kind === 'Embedding';
  return (
    <div className="wiz-endpoint">
      <div className="wiz-endpoint-title" title={isEmbed
        ? 'The embedding model turns text into vectors for semantic search. It must match your collection dimensionality.'
        : 'The completion model writes grounded answers from retrieved material and drives the agentic chat tools.'}>
        {isEmbed ? 'Embedding model' : 'Completion model'}
      </div>
      <div className="wiz-grid">
        <Field label="Name" tip="A label for this endpoint shown in the Model Runners list — e.g. 'local-embed'.">
          <input value={value.name} onChange={(e) => set({ name: e.target.value })} placeholder={isEmbed ? 'local-embed' : 'local-chat'} />
        </Field>
        <Field label="Model" tip={isEmbed ? "The provider's embedding model id, e.g. 'nomic-embed-text'." : "The provider's chat/completion model id, e.g. 'llama3.2'."}>
          <input value={value.model} onChange={(e) => set({ model: e.target.value })} placeholder={isEmbed ? 'nomic-embed-text' : 'llama3.2'} />
        </Field>
        <Field label="Endpoint URL" tip="Base URL of the provider that serves this model. For the bundled Ollama use http://ollama:11434.">
          <input value={value.endpoint} onChange={(e) => set({ endpoint: e.target.value })} placeholder="http://ollama:11434" />
        </Field>
        <Field label="API format" tip="Which provider API this endpoint speaks. Ollama for the bundled local runner; OpenAI/Gemini for hosted APIs.">
          <select value={value.apiFormat} onChange={(e) => set({ apiFormat: e.target.value })}>
            {API_FORMATS.map((f) => <option key={f} value={f}>{f}</option>)}
          </select>
        </Field>
        <Field label="API key" tip="Secret key for hosted providers (OpenAI/Gemini). Leave blank for a local Ollama, which needs none.">
          <input type="password" value={value.apiKey} onChange={(e) => set({ apiKey: e.target.value })} placeholder="(none for Ollama)" />
        </Field>
        <Field label="Max concurrency" tip="Upper bound on simultaneous requests Partio opens to this endpoint. Keep low (2) for a local model.">
          <input type="number" min="1" value={value.maxConcurrentRequests} onChange={(e) => set({ maxConcurrentRequests: e.target.value })} />
        </Field>
      </div>
    </div>
  );
}

const blankEndpoint = (apiFormat = 'Ollama') => ({ name: '', model: '', endpoint: 'http://ollama:11434', apiFormat, apiKey: '', maxConcurrentRequests: 1 });

export default function SetupWizard({ onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();

  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const [embed, setEmbed] = useState(blankEndpoint());
  const [complete, setComplete] = useState(blankEndpoint());
  const [endpointIds, setEndpointIds] = useState({ embedding: null, completion: null });

  const [subject, setSubject] = useState({ displayName: '', type: 'Person', description: '' });
  const [subjectId, setSubjectId] = useState(null);

  const [link, setLink] = useState({ url: '', title: '' });
  const [collections, setCollections] = useState([]);
  const [collectionId, setCollectionId] = useState('');
  const [linkId, setLinkId] = useState(null);

  const [jobDetail, setJobDetail] = useState(null);
  const pollRef = useRef(null);

  const finish = useCallback(() => {
    try { localStorage.setItem('pneuma.setupComplete', '1'); } catch { /* ignore */ }
    onClose();
    navigate('/dashboard/ask');
  }, [navigate, onClose]);

  const skip = useCallback(() => {
    try { localStorage.setItem('pneuma.setupComplete', '1'); } catch { /* ignore */ }
    onClose();
  }, [onClose]);

  // Load collections when reaching the link step (the default collection is provisioned with the tenant).
  useEffect(() => {
    if (step !== 2) return;
    apiClient.listCollections()
      .then((resp) => {
        const items = normalizeList(resp).items;
        setCollections(items);
        if (items.length > 0 && !collectionId) setCollectionId(items[0].id);
      })
      .catch(() => { /* surfaced on submit */ });
  }, [step, apiClient, collectionId]);

  // Poll the ingestion job while on the final step until it reaches a terminal state.
  useEffect(() => {
    if (step !== 3 || !linkId) return undefined;
    let cancelled = false;
    const poll = async () => {
      try {
        const runs = normalizeList(await apiClient.getLinkIngestionLog(linkId)).items;
        const latest = runs.length > 0 ? runs[runs.length - 1] : null;
        if (!cancelled && latest) setJobDetail(latest);
        const status = latest?.job?.status || latest?.Job?.status;
        if (status && isTerminal(status) && pollRef.current) { clearInterval(pollRef.current); pollRef.current = null; }
      } catch { /* keep last */ }
    };
    poll();
    pollRef.current = setInterval(poll, 3000);
    return () => { cancelled = true; if (pollRef.current) { clearInterval(pollRef.current); pollRef.current = null; } };
  }, [step, linkId, apiClient]);

  const createEndpoints = async () => {
    const mk = (type, f) => apiClient.create('model-runners', {
      type, name: f.name || type, model: f.model, endpoint: f.endpoint, apiFormat: f.apiFormat,
      apiKey: f.apiKey || undefined, active: true, maxConcurrentRequests: Number(f.maxConcurrentRequests) || 1
    });
    const e = await mk('Embedding', embed);
    const c = await mk('Completion', complete);
    setEndpointIds({ embedding: e.id || e.Id, completion: c.id || c.Id });
  };

  const createSubject = async () => {
    const created = await apiClient.create('subjects', { displayName: subject.displayName, type: subject.type || undefined, description: subject.description || undefined });
    setSubjectId(created.id || created.Id);
  };

  const createLink = async () => {
    const created = await apiClient.create(`subjects/${encodeURIComponent(subjectId)}/links`, {
      url: link.url, title: link.title || undefined,
      embeddingEndpointId: endpointIds.embedding, completionEndpointId: endpointIds.completion, collectionId
    });
    setLinkId(created.id || created.Id);
  };

  const canAdvance = () => {
    if (step === 0) return embed.model && embed.endpoint && complete.model && complete.endpoint;
    if (step === 1) return subject.displayName.trim();
    if (step === 2) return link.url.trim() && collectionId;
    return true;
  };

  const next = async () => {
    setError('');
    setBusy(true);
    try {
      if (step === 0) await createEndpoints();
      else if (step === 1) await createSubject();
      else if (step === 2) await createLink();
      setStep((s) => s + 1);
    } catch (err) {
      setError(err?.message || 'Something went wrong. Please check your inputs and try again.');
    } finally {
      setBusy(false);
    }
  };

  const jobData = jobDetail?.job || jobDetail?.Job || {};
  const events = normalizeList(jobDetail?.events || jobDetail?.Events || []).items;
  const jobStatus = jobData.status || jobData.Status;
  const jobDone = jobStatus && isTerminal(jobStatus);
  const meta = STEPS[step];

  const footer = (
    <>
      <button type="button" className="button-secondary" onClick={skip} title="Close the wizard and set things up yourself later from the sidebar.">
        {t('setup.skip', 'Skip setup')}
      </button>
      <div style={{ flex: 1 }} />
      {step > 0 && step < 3 && (
        <button type="button" className="button-secondary" onClick={() => setStep((s) => s - 1)} disabled={busy}
          title="Return to the previous step. Anything already created is kept.">
          {t('common.back', 'Back')}
        </button>
      )}
      {step < 3 && (
        <button type="button" className="button-primary" onClick={next} disabled={busy || !canAdvance()}
          title={step === 2 ? 'Create the source link and start ingestion.' : 'Save this step and continue.'}>
          {busy ? t('common.loading', 'Working…') : t('common.continue', 'Continue')}
        </button>
      )}
      {step === 3 && (
        <button type="button" className="button-primary" onClick={finish} disabled={!jobDone}
          title={jobDone ? 'Go to the Ask page and query your newly ingested content.' : 'Available once ingestion finishes.'}>
          {t('setup.goToAsk', 'Go to Ask')}
        </button>
      )}
    </>
  );

  return (
    <Modal title={t('setup.title', 'Welcome to Pneuma — quick setup')} subtitle={meta.hint} size="wide" onClose={skip} footer={footer}>
      <ol className="wiz-steps" aria-label="Setup progress">
        {STEPS.map((s, i) => (
          <li key={s.key} className={`wiz-step ${i === step ? 'is-active' : ''} ${i < step ? 'is-done' : ''}`} title={s.hint}>
            <span className="wiz-step-num">{i < step ? '✓' : i + 1}</span>
            <span className="wiz-step-title">{s.title}</span>
          </li>
        ))}
      </ol>

      {error && <div className="error-message" style={{ marginBottom: '0.75rem' }}>{error}</div>}

      {step === 0 && (
        <div className="wiz-panel">
          <EndpointForm kind="Embedding" value={embed} onChange={setEmbed} />
          <EndpointForm kind="Completion" value={complete} onChange={setComplete} />
        </div>
      )}

      {step === 1 && (
        <div className="wiz-panel wiz-grid">
          <Field label="Display name" tip="What this subject is called. Everything you ingest here is scoped to it — e.g. 'Ada Lovelace' or 'Acme CRM'.">
            <input value={subject.displayName} onChange={(e) => setSubject({ ...subject, displayName: e.target.value })} placeholder="Ada Lovelace" autoFocus />
          </Field>
          <Field label="Type" tip="A free-form category for the subject (Person, Product, Topic…). Purely descriptive; it does not restrict ingestion.">
            <input value={subject.type} onChange={(e) => setSubject({ ...subject, type: e.target.value })} placeholder="Person" />
          </Field>
          <Field label="Description" tip="Optional context about this subject, shown in the subjects list to help operators tell them apart.">
            <textarea rows={3} value={subject.description} onChange={(e) => setSubject({ ...subject, description: e.target.value })} />
          </Field>
        </div>
      )}

      {step === 2 && (
        <div className="wiz-panel wiz-grid">
          <Field label="Source URL" tip="A web page (or document URL) to ingest into this subject. Pneuma crawls it, extracts entities, and indexes the text.">
            <input value={link.url} onChange={(e) => setLink({ ...link, url: e.target.value })} placeholder="https://en.wikipedia.org/wiki/Ada_Lovelace" autoFocus />
          </Field>
          <Field label="Title" tip="Optional friendly name for this source. Defaults to the page title when left blank.">
            <input value={link.title} onChange={(e) => setLink({ ...link, title: e.target.value })} placeholder="(optional)" />
          </Field>
          <Field label="Collection" tip="The RecallDB vector collection this source's chunks are indexed into. The default was created with your tenant.">
            <select value={collectionId} onChange={(e) => setCollectionId(e.target.value)}>
              {collections.length === 0 && <option value="">No collection available</option>}
              {collections.map((c) => <option key={c.id} value={c.id}>{c.name || c.id}</option>)}
            </select>
          </Field>
        </div>
      )}

      {step === 3 && (
        <div className="wiz-panel">
          <p className="wiz-ingest-note" title="The pipeline runs type detection → cell extraction → knowledge-graph mapping → embedding → indexing.">
            {jobDone
              ? t('setup.ingestDone', 'Ingestion complete — your content is ready to query.')
              : t('setup.ingestRunning', 'Ingesting your source. This can take a minute or two on a local model…')}
          </p>
          <IngestionTimeline jobData={jobData} events={events} />
          {!jobDetail && <div className="table-loading"><div className="loading-spinner" /></div>}
        </div>
      )}
    </Modal>
  );
}
