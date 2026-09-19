import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';
import FacetFilterEditor from '../components/FacetFilterEditor';
import ConcurrencyOverridesEditor from '../components/ConcurrencyOverridesEditor';


// Sensible starter prompts pre-filled when creating a subject. Appended after the global prompts, so they
// refine (not replace) the platform defaults; operators can edit or clear them.
const DEFAULT_SYSTEM_PROMPT =
  'Focus your answers on this subject. Prefer its ingested sources, be precise about names, dates, and relationships, '
  + 'and clearly say when the archive does not cover something. Never expose internal identifiers or system internals to '
  + 'the user: do not print node ids, GUIDs, collection or job ids, or other database keys, and never mention Pneuma’s '
  + 'internal object kinds or storage labels such as "Cell", "Chunk", or "Source node" (for example, never write '
  + '"(source: Cell)"). Refer to sources by their human-readable title or a short quotation.';
const DEFAULT_ONTOLOGY_CLASSIFY =
  'Identify the entities (people, organizations, works, events, places, and themes) and the relationships among them '
  + "that are relevant to this subject, and map them into the subject's knowledge-graph ontology.";
const DEFAULT_ONTOLOGY_DEFINITION =
  'Entities: Person, Organization, Work, Event, Place, Theme. '
  + 'Relationships: created, contributed-to, participated-in, located-in, part-of, influenced, associated-with.';
// Default ask-page subtitle, mirroring the backend Subject.DefaultTagline and the user dashboard's built-in label.
const DEFAULT_TAGLINE = 'Get an answer grounded in the archive, with the sources that support it.';
// Default subject-level rerank/rewrite prompts, mirroring the backend Subject defaults.
const DEFAULT_RERANKING_PROMPT = 'Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.';
const DEFAULT_PROMPT_REWRITE = 'Rewrite the question into a single, self-contained search query for this subject’s archive: resolve references, expand abbreviations, and keep it concise.';

const EMPTY_FORM = {
  displayName: '', type: 'Subject', description: '', tagline: DEFAULT_TAGLINE, urlSlug: '',
  thinkingEnabled: false, publishedForChat: true, historyRetentionDays: 90,
  embeddingModel: '', inferenceModel: '', collection: '', rerankingModel: '', promptRewriteModel: '',
  rerankerType: 'LlmListwise',
  chunkStrategy: 'FixedTokenCount', chunkMaxTokens: 256, chunkOverlapTokens: 32,
  systemPrompt: DEFAULT_SYSTEM_PROMPT,
  ontologyClassifyPrompt: DEFAULT_ONTOLOGY_CLASSIFY,
  ontologyDefinitionPrompt: DEFAULT_ONTOLOGY_DEFINITION,
  rerankingPrompt: DEFAULT_RERANKING_PROMPT,
  promptRewritePrompt: DEFAULT_PROMPT_REWRITE,
  retrievalFilterJson: '',
  concurrencyOverrides: {}
};

function endpointLabel(ep) { return ep.name || ep.model || ep.id; }
function collectionLabel(c) {
  const name = c.name || c.Name || c.id || c.Id;
  const dims = c.dimensionality ?? c.Dimensionality;
  return dims ? `${name} (${dims}d)` : name;
}

// Slug: lowercase, collapse non-alphanumeric runs to single dashes, trim dashes.
function slugify(value) {
  return String(value || '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

function SubjectsView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();

  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [embeddingEndpoints, setEmbeddingEndpoints] = useState([]);
  const [completionEndpoints, setCompletionEndpoints] = useState([]);
  const [collections, setCollections] = useState([]);
  const [ingestionDefaults, setIngestionDefaults] = useState(null);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);
  const [deletingNotice, setDeletingNotice] = useState(false);

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.getSubjects({ maxResults: 1000 });
      setSubjects(asArray(res, 'subjects'));
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    load();
  }, [load]);

  // Load available models + collections for the subject's model pickers.
  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    (async () => {
      const [e, col, ing] = await Promise.allSettled([
        apiClient.listIngestionEndpoints(),
        apiClient.listCollections(),
        apiClient.getIngestionSettings()
      ]);
      if (cancelled) return;
      if (e.status === 'fulfilled') {
        setEmbeddingEndpoints(asArray(e.value?.embedding).filter((x) => x.active !== false));
        setCompletionEndpoints(asArray(e.value?.completion).filter((x) => x.active !== false));
      }
      if (col.status === 'fulfilled') setCollections(asArray(col.value).filter((x) => (x.active ?? x.Active) !== false));
      // System-default ingestion concurrency populates the placeholders in the overrides editor (best-effort).
      if (ing.status === 'fulfilled') setIngestionDefaults(ing.value || {});
    })();
    return () => { cancelled = true; };
  }, [apiClient]);

  const soleId = (list) => (list.length === 1 ? (list[0].id ?? list[0].Id) : '');

  const openCreate = () => {
    setEditing(null);
    // Pre-select the sole available model/collection so a subject is usable out of the box.
    setForm({ ...EMPTY_FORM, embeddingModel: soleId(embeddingEndpoints), inferenceModel: soleId(completionEndpoints), collection: soleId(collections) });
    setFormError('');
    setFormOpen(true);
  };

  const openEdit = (subject) => {
    setEditing(subject);
    setForm({
      displayName: subject.displayName || '',
      type: subject.type || 'Subject',
      description: subject.description || '',
      tagline: subject.tagline != null ? subject.tagline : DEFAULT_TAGLINE,
      urlSlug: subject.urlSlug || '',
      thinkingEnabled: !!subject.thinkingEnabled,
      publishedForChat: subject.publishedForChat !== false,
      historyRetentionDays: subject.historyRetentionDays || 90,
      embeddingModel: subject.embeddingModel || '',
      inferenceModel: subject.inferenceModel || '',
      collection: subject.collection || '',
      chunkStrategy: subject.chunkStrategy || 'FixedTokenCount',
      chunkMaxTokens: subject.chunkMaxTokens || 256,
      chunkOverlapTokens: subject.chunkOverlapTokens != null ? subject.chunkOverlapTokens : 32,
      rerankingModel: subject.rerankingModel || '',
      rerankerType: subject.rerankerType || 'LlmListwise',
      promptRewriteModel: subject.promptRewriteModel || '',
      systemPrompt: subject.systemPrompt || '',
      ontologyClassifyPrompt: subject.ontologyClassifyPrompt || '',
      ontologyDefinitionPrompt: subject.ontologyDefinitionPrompt || '',
      rerankingPrompt: subject.rerankingPrompt != null ? subject.rerankingPrompt : DEFAULT_RERANKING_PROMPT,
      promptRewritePrompt: subject.promptRewritePrompt != null ? subject.promptRewritePrompt : DEFAULT_PROMPT_REWRITE,
      retrievalFilterJson: subject.retrievalFilterJson || '',
      concurrencyOverrides: (subject.concurrencyOverrides && typeof subject.concurrencyOverrides === 'object') ? subject.concurrencyOverrides : {}
    });
    setFormError('');
    setFormOpen(true);
  };

  const handleSave = async (e) => {
    e.preventDefault();
    if (!form.displayName.trim()) {
      setFormError('Display name is required.');
      return;
    }
    if (!form.embeddingModel || !form.inferenceModel || !form.collection) {
      setFormError(t('subjects.modelsRequired', 'An embedding model, inference model, and collection are required before this subject can ingest links or answer questions.'));
      return;
    }
    if (form.retrievalFilterJson && form.retrievalFilterJson.trim()) {
      try { JSON.parse(form.retrievalFilterJson); }
      catch { setFormError(t('subjects.filterInvalid', 'The retrieval filter must be valid JSON, or left blank.')); return; }
    }
    setSaving(true);
    setFormError('');
    const payload = {
      ...form,
      urlSlug: form.urlSlug ? slugify(form.urlSlug) : slugify(form.displayName),
      thinkingEnabled: !!form.thinkingEnabled,
      publishedForChat: !!form.publishedForChat,
      chunkStrategy: form.chunkStrategy || 'FixedTokenCount',
      chunkMaxTokens: Math.max(16, Number(form.chunkMaxTokens) || 256),
      chunkOverlapTokens: Math.max(0, Number(form.chunkOverlapTokens) || 0),
      historyRetentionDays: Math.max(1, Number(form.historyRetentionDays) || 90),
      // Only the concurrency knobs the operator set are sent as overrides; the rest inherit the system default.
      concurrencyOverrides: Object.fromEntries(
        Object.entries(form.concurrencyOverrides || {})
          .filter(([, v]) => v !== null && v !== undefined && v !== '')
          .map(([k, v]) => [k, Number(v)])
      )
    };
    try {
      if (editing) {
        await apiClient.updateSubject(editing.id, payload);
      } else {
        await apiClient.createSubject(payload);
      }
      setFormOpen(false);
      await load();
    } catch (err) {
      setFormError(err.message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await apiClient.deleteSubject(deleteTarget.id);
      setDeleteTarget(null);
      setDeletingNotice(true);
      await load();
    } catch (err) {
      setError(err.message);
    } finally {
      setDeleting(false);
    }
  };

  const columns = [
    {
      key: 'displayName',
      label: t('subjects.displayName'),
      render: (v, row) => (row.deletionStatus && row.deletionStatus !== 'None')
        ? <span style={{ opacity: 0.5 }}><strong>{v || '(unnamed)'}</strong> · <em>{row.deletionStatus === 'Failed' ? t('subjects.deletionFailed', 'deletion failed') : t('subjects.deleting', 'deleting…')}</em></span>
        : <strong>{v || '(unnamed)'}</strong>
    },
    { key: 'type', label: t('subjects.type') },
    {
      key: 'description',
      label: t('subjects.description'),
      sortable: false,
      render: (v) => <span title={v}>{v || '—'}</span>
    },
    {
      key: 'publishedForChat',
      label: t('subjects.consumerChat', 'Consumer Chat'),
      sortable: false,
      render: (v) => v === false
        ? <span style={{ opacity: 0.6 }}>{t('subjects.hidden', 'Hidden')}</span>
        : <span>{t('subjects.published', 'Published')}</span>
    },
    {
      key: 'id',
      label: 'ID',
      className: 'cell-id',
      sortable: false,
      render: (v) => <CopyableId value={v} title="Copy subject ID" />
    },
    {
      key: '_actions',
      label: t('common.actions'),
      className: 'actions-column',
      sortable: false,
      render: (_v, row) => (
        <ActionMenu
          actions={[
            { label: t('common.edit'), onClick: () => openEdit(row) },
            { label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(row) }
          ]}
        />
      )
    }
  ];

  return (
    <div>
      <PageHeader
        title={t('subjects.title')}
        subtitle={t('subjects.subtitle')}
        actions={
          <button className="btn btn-primary" onClick={openCreate}>
            {t('subjects.add')}
          </button>
        }
      />

      {error && <div className="error-banner">{error}</div>}

      <DataTable
        columns={columns}
        data={subjects}
        loading={loading}
        onRefresh={load}
        emptyTitle={t('subjects.title')}
        emptyDescription={t('subjects.empty')}
      />

      <Modal
        isOpen={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? t('subjects.editTitle') : t('subjects.createTitle')}
        size="medium"
      >
        <form onSubmit={handleSave}>
          {formError && <div className="form-error">{formError}</div>}
          <div className="form-group" title={t('subjects.displayNameTip', 'The name of the subject this archive is about (for example, a person, product, company, place, or topic). All content ingested is scoped to it. Required.')}>
            <label htmlFor="cd-name" title={t('subjects.displayNameTip', 'The name of the subject this archive is about (for example, a person, product, company, place, or topic). All content ingested is scoped to it. Required.')}>
              {t('subjects.displayName')} <span className="required-mark">*</span>
            </label>
            <input
              id="cd-name"
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              required
              title={t('subjects.displayNameTip', 'The name of the subject this archive is about (for example, a person, product, company, place, or topic). All content ingested is scoped to it. Required.')}
            />
          </div>
          <div className="form-group" title={t('subjects.typeTip', 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.')}>
            <label htmlFor="cd-type" title={t('subjects.typeTip', 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.')}>{t('subjects.type')}</label>
            <input
              id="cd-type"
              type="text"
              value={form.type}
              placeholder="Subject"
              onChange={(e) => setForm({ ...form, type: e.target.value })}
              title={t('subjects.typeTip', 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.')}
            />
          </div>
          <div className="form-group" title={t('subjects.descriptionTip', 'Optional notes shown in the subjects list to help you tell similar subjects apart.')}>
            <label htmlFor="cd-desc" title={t('subjects.descriptionTip', 'Optional notes shown in the subjects list to help you tell similar subjects apart.')}>{t('subjects.description')}</label>
            <textarea
              id="cd-desc"
              rows={4}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              title={t('subjects.descriptionTip', 'Optional notes shown in the subjects list to help you tell similar subjects apart.')}
            />
          </div>
          <div className="form-group" title={t('subjects.taglineTip', "The subtitle shown beneath this subject's name on its ask page in the user dashboard (under the search box before asking, and under the chat header after). A sensible default is supplied; edit it to set the tone for this subject.")}>
            <label htmlFor="cd-tagline" title={t('subjects.taglineTip', "The subtitle shown beneath this subject's name on its ask page in the user dashboard (under the search box before asking, and under the chat header after). A sensible default is supplied; edit it to set the tone for this subject.")}>{t('subjects.tagline', 'Ask-Page Tagline')}</label>
            <textarea
              id="cd-tagline"
              rows={2}
              value={form.tagline}
              placeholder={DEFAULT_TAGLINE}
              onChange={(e) => setForm({ ...form, tagline: e.target.value })}
              title={t('subjects.taglineTip', "The subtitle shown beneath this subject's name on its ask page in the user dashboard (under the search box before asking, and under the chat header after). A sensible default is supplied; edit it to set the tone for this subject.")}
            />
          </div>
          <div className="form-group" title={t('subjects.urlSlugTip', 'URL-safe slug used to reach this subject in the user dashboard (must be unique within the tenant). Auto-derived from the display name when left blank.')}>
            <label htmlFor="cd-slug" title={t('subjects.urlSlugTip', 'URL-safe slug used to reach this subject in the user dashboard (must be unique within the tenant). Auto-derived from the display name when left blank.')}>{t('subjects.urlSlug', 'URL Slug')}</label>
            <input
              id="cd-slug"
              type="text"
              value={form.urlSlug}
              placeholder={slugify(form.displayName) || 'derived-from-name'}
              onChange={(e) => setForm({ ...form, urlSlug: e.target.value })}
              title={t('subjects.urlSlugTip', 'URL-safe slug used to reach this subject in the user dashboard (must be unique within the tenant). Auto-derived from the display name when left blank.')}
            />
            <div className="field-hint">{t('subjects.urlSlugHint', 'URL-safe slug used to reach this subject in the user dashboard. Must be unique.')}</div>
          </div>
          <div className="form-group" style={{ display: 'flex', alignItems: 'center', gap: 8 }} title={t('subjects.thinkingEnabledTip', "When on, the model's reasoning is shown in a collapsible section (with a thinking-time statistic) for chats about this subject. Off hides it.")}>
            <input
              id="cd-thinking"
              type="checkbox"
              style={{ width: 'auto' }}
              checked={form.thinkingEnabled}
              onChange={(e) => setForm({ ...form, thinkingEnabled: e.target.checked })}
              title={t('subjects.thinkingEnabledTip', "When on, the model's reasoning is shown in a collapsible section (with a thinking-time statistic) for chats about this subject. Off hides it.")}
            />
            <label htmlFor="cd-thinking" style={{ margin: 0 }} title={t('subjects.thinkingEnabledTip', "When on, the model's reasoning is shown in a collapsible section (with a thinking-time statistic) for chats about this subject. Off hides it.")}>{t('subjects.thinkingEnabled', 'Show model thinking in chat')}</label>
          </div>
          <div className="form-group" style={{ display: 'flex', alignItems: 'center', gap: 8 }} title={t('subjects.publishedForChatTip', 'When on, this subject appears in the end-user (consumer) dashboard and its ask page is reachable. Turn it off to withhold it from end users while you review its archive; this dashboard always sees it. On by default.')}>
            <input
              id="cd-published"
              type="checkbox"
              style={{ width: 'auto' }}
              checked={form.publishedForChat}
              onChange={(e) => setForm({ ...form, publishedForChat: e.target.checked })}
              title={t('subjects.publishedForChatTip', 'When on, this subject appears in the end-user (consumer) dashboard and its ask page is reachable. Turn it off to withhold it from end users while you review its archive; this dashboard always sees it. On by default.')}
            />
            <label htmlFor="cd-published" style={{ margin: 0 }} title={t('subjects.publishedForChatTip', 'When on, this subject appears in the end-user (consumer) dashboard and its ask page is reachable. Turn it off to withhold it from end users while you review its archive; this dashboard always sees it. On by default.')}>{t('subjects.publishedForChat', 'Available in consumer chat')}</label>
          </div>
          <div className="form-group" title={t('subjects.historyRetentionTip', 'How many days of chat-turn history are kept for this subject before pruning. Minimum 1. Default 90.')}>
            <label htmlFor="cd-retention" title={t('subjects.historyRetentionTip', 'How many days of chat-turn history are kept for this subject before pruning. Minimum 1. Default 90.')}>{t('subjects.historyRetentionDays', 'Chat History Retention (days)')}</label>
            <input
              id="cd-retention"
              type="number"
              min={1}
              value={form.historyRetentionDays}
              onChange={(e) => setForm({ ...form, historyRetentionDays: e.target.value })}
              title={t('subjects.historyRetentionTip', 'How many days of chat-turn history are kept for this subject before pruning. Minimum 1. Default 90.')}
            />
          </div>
          <div className="form-group" title={t('subjects.embeddingModelTip', 'The embedding endpoint used to vectorize this subject’s content at ingestion and to embed queries when answering. Must match the collection’s dimensionality. Required to ingest links.')}>
            <label htmlFor="cd-embedding" title={t('subjects.embeddingModelTip', 'The embedding endpoint used to vectorize this subject’s content and queries. Required.')}>{t('subjects.embeddingModel', 'Embedding Model')} <span className="required-mark">*</span></label>
            <select id="cd-embedding" value={form.embeddingModel} required onChange={(e) => setForm({ ...form, embeddingModel: e.target.value })}
              title={t('subjects.embeddingModelTip', 'The embedding endpoint used to vectorize this subject’s content and queries. Required.')}>
              <option value="" disabled>{t('subjects.selectModel', 'Select a model')}</option>
              {embeddingEndpoints.map((ep) => <option key={ep.id} value={ep.id}>{endpointLabel(ep)}</option>)}
            </select>
          </div>
          <div className="form-group" title={t('subjects.inferenceModelTip', 'The completion endpoint used for this subject’s ingestion inference and answer generation. Required to ingest links or answer questions.')}>
            <label htmlFor="cd-inference" title={t('subjects.inferenceModelTip', 'The completion endpoint used for this subject’s ingestion inference and answers. Required.')}>{t('subjects.inferenceModel', 'Inference Model')} <span className="required-mark">*</span></label>
            <select id="cd-inference" value={form.inferenceModel} required onChange={(e) => setForm({ ...form, inferenceModel: e.target.value })}
              title={t('subjects.inferenceModelTip', 'The completion endpoint used for this subject’s ingestion inference and answers. Required.')}>
              <option value="" disabled>{t('subjects.selectModel', 'Select a model')}</option>
              {completionEndpoints.map((ep) => <option key={ep.id} value={ep.id}>{endpointLabel(ep)}</option>)}
            </select>
          </div>
          <div className="form-group" title={t('subjects.collectionTip', 'The RecallDB collection where this subject’s chunks are stored and searched. Choose one whose dimensionality matches the embedding model. Required to ingest links.')}>
            <label htmlFor="cd-collection" title={t('subjects.collectionTip', 'The RecallDB collection where this subject’s chunks are stored and searched. Required.')}>{t('subjects.collection', 'Collection')} <span className="required-mark">*</span></label>
            <select id="cd-collection" value={form.collection} required onChange={(e) => setForm({ ...form, collection: e.target.value })}
              title={t('subjects.collectionTip', 'The RecallDB collection where this subject’s chunks are stored and searched. Required.')}>
              <option value="" disabled>{t('subjects.selectCollection', 'Select a collection')}</option>
              {collections.map((c) => <option key={c.id ?? c.Id} value={c.id ?? c.Id}>{collectionLabel(c)}</option>)}
            </select>
          </div>
          <div className="form-group" title={t('subjects.chunkStrategyTip', 'How this subject’s content is split into chunks for retrieval. Fixed token count uses the size and overlap below; sentence- and paragraph-based split on natural boundaries. Applies to new ingestions.')}>
            <label htmlFor="cd-chunkstrategy">{t('subjects.chunkStrategy', 'Chunking Strategy')}</label>
            <select id="cd-chunkstrategy" value={form.chunkStrategy} onChange={(e) => setForm({ ...form, chunkStrategy: e.target.value })}>
              <option value="FixedTokenCount">{t('subjects.chunkFixed', 'Fixed token count')}</option>
              <option value="SentenceBased">{t('subjects.chunkSentence', 'Sentence based')}</option>
              <option value="ParagraphBased">{t('subjects.chunkParagraph', 'Paragraph based')}</option>
            </select>
          </div>
          <div className="form-group" title={t('subjects.chunkMaxTokensTip', 'Target chunk size in tokens for fixed-token-count chunking. Larger chunks give more context per hit; smaller chunks give finer-grained retrieval. Default 256.')}>
            <label htmlFor="cd-chunksize">{t('subjects.chunkMaxTokens', 'Chunk Size (tokens)')}</label>
            <input id="cd-chunksize" type="number" min="16" value={form.chunkMaxTokens} onChange={(e) => setForm({ ...form, chunkMaxTokens: e.target.value })} />
          </div>
          <div className="form-group" title={t('subjects.chunkOverlapTip', 'How many tokens adjacent chunks share, so context is not lost at chunk boundaries. Default 32.')}>
            <label htmlFor="cd-chunkoverlap">{t('subjects.chunkOverlapTokens', 'Chunk Overlap (tokens)')}</label>
            <input id="cd-chunkoverlap" type="number" min="0" value={form.chunkOverlapTokens} onChange={(e) => setForm({ ...form, chunkOverlapTokens: e.target.value })} />
          </div>
          <div className="form-group" title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages by relevance before answering. Leave as None to skip reranking.')}>
            <label htmlFor="cd-rerankmodel" title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages before answering. None skips reranking.')}>{t('subjects.rerankingModel', 'Reranking Model (optional)')}</label>
            <select id="cd-rerankmodel" value={form.rerankingModel} onChange={(e) => setForm({ ...form, rerankingModel: e.target.value })}
              title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages before answering. None skips reranking.')}>
              <option value="">{t('subjects.none', 'None')}</option>
              {completionEndpoints.map((ep) => <option key={ep.id} value={ep.id}>{endpointLabel(ep)}</option>)}
            </select>
          </div>
          <div className="form-group" title={t('subjects.rerankerTypeTip', 'How retrieved passages are reordered before answering. LLM listwise uses the reranking model above; cross-encoder uses the globally-configured rerank endpoint and falls back to LLM listwise when none is configured.')}>
            <label htmlFor="cd-rerankertype">{t('subjects.rerankerType', 'Reranker Type')}</label>
            <select id="cd-rerankertype" value={form.rerankerType} onChange={(e) => setForm({ ...form, rerankerType: e.target.value })}>
              <option value="LlmListwise">{t('subjects.rerankerLlm', 'LLM listwise')}</option>
              <option value="CrossEncoder">{t('subjects.rerankerCrossEncoder', 'Cross-encoder (dedicated endpoint)')}</option>
            </select>
          </div>
          <div className="form-group" title={t('subjects.promptRewriteModelTip', 'Optional completion endpoint used to rewrite the user’s question into a retrieval query before searching. Leave as None to skip prompt rewriting.')}>
            <label htmlFor="cd-rewritemodel" title={t('subjects.promptRewriteModelTip', 'Optional completion endpoint used to rewrite the question into a retrieval query. None skips prompt rewriting.')}>{t('subjects.promptRewriteModel', 'Prompt Rewrite Model (optional)')}</label>
            <select id="cd-rewritemodel" value={form.promptRewriteModel} onChange={(e) => setForm({ ...form, promptRewriteModel: e.target.value })}
              title={t('subjects.promptRewriteModelTip', 'Optional completion endpoint used to rewrite the question into a retrieval query. None skips prompt rewriting.')}>
              <option value="">{t('subjects.none', 'None')}</option>
              {completionEndpoints.map((ep) => <option key={ep.id} value={ep.id}>{endpointLabel(ep)}</option>)}
            </select>
          </div>
          <div className="form-group">
            <div className="field-hint" style={{ padding: '0.5rem 0', lineHeight: 1.5 }}>
              {t('subjects.promptsMovedNote', 'Prompt overrides (system, reranking, prompt rewrite, and ontology prompts) are now managed per subject on the Prompts page, which shows the effective content, the global default it falls back to, and the merge mode for each prompt.')}
            </div>
          </div>
          <div className="form-group" title={t('subjects.retrievalFilterTip', 'Optional default facet filter restricting which ingested chunks answers may draw on. Add required/excluded labels (e.g. html, pdf) and tag key/value pairs. Leave empty for no filter.')}>
            <label title={t('subjects.retrievalFilterTip', 'Optional default facet filter restricting which ingested chunks answers may draw on.')}>{t('subjects.retrievalFilter', 'Retrieval Filter (optional)')}</label>
            <FacetFilterEditor value={form.retrievalFilterJson} onChange={(json) => setForm({ ...form, retrievalFilterJson: json })} />
          </div>
          <ConcurrencyOverridesEditor
            value={form.concurrencyOverrides}
            onChange={(next) => setForm({ ...form, concurrencyOverrides: next })}
            defaults={ingestionDefaults}
          />
          <div className="form-actions">
            <button type="button" className="btn btn-secondary" onClick={() => setFormOpen(false)} disabled={saving}>
              {t('common.cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? t('common.loading') : editing ? t('common.save') : t('common.create')}
            </button>
          </div>
        </form>
      </Modal>

      <ConfirmModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('common.delete')}
        message={`Delete this subject and everything associated with it? This cannot be undone.`}
        entityName={deleteTarget?.displayName}
        confirmLabel={t('common.delete')}
        isLoading={deleting}
      />

      <Modal
        isOpen={deletingNotice}
        onClose={() => setDeletingNotice(false)}
        title={t('subjects.deletingTitle', 'Deleting in the background')}
      >
        <p>{t('subjects.deletingBackground', 'We are deleting this subject and everything associated with it in the background. You may close this window.')}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-primary" onClick={() => setDeletingNotice(false)}>{t('common.close')}</button>
        </div>
      </Modal>
    </div>
  );
}

export default SubjectsView;
