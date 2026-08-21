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


// Sensible starter prompts pre-filled when creating a subject. Appended after the global prompts, so they
// refine (not replace) the platform defaults; operators can edit or clear them.
const DEFAULT_SYSTEM_PROMPT =
  'Focus your answers on this subject. Prefer its ingested sources, be precise about names, dates, and relationships, '
  + 'and clearly say when the archive does not cover something.';
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
  thinkingEnabled: false, historyRetentionDays: 90,
  embeddingModel: '', inferenceModel: '', collection: '', rerankingModel: '', promptRewriteModel: '',
  systemPrompt: DEFAULT_SYSTEM_PROMPT,
  ontologyClassifyPrompt: DEFAULT_ONTOLOGY_CLASSIFY,
  ontologyDefinitionPrompt: DEFAULT_ONTOLOGY_DEFINITION,
  rerankingPrompt: DEFAULT_RERANKING_PROMPT,
  promptRewritePrompt: DEFAULT_PROMPT_REWRITE
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
      const [e, col] = await Promise.allSettled([apiClient.listIngestionEndpoints(), apiClient.listCollections()]);
      if (cancelled) return;
      if (e.status === 'fulfilled') {
        setEmbeddingEndpoints(asArray(e.value?.embedding).filter((x) => x.active !== false));
        setCompletionEndpoints(asArray(e.value?.completion).filter((x) => x.active !== false));
      }
      if (col.status === 'fulfilled') setCollections(asArray(col.value).filter((x) => (x.active ?? x.Active) !== false));
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
      historyRetentionDays: subject.historyRetentionDays || 90,
      embeddingModel: subject.embeddingModel || '',
      inferenceModel: subject.inferenceModel || '',
      collection: subject.collection || '',
      rerankingModel: subject.rerankingModel || '',
      promptRewriteModel: subject.promptRewriteModel || '',
      systemPrompt: subject.systemPrompt || '',
      ontologyClassifyPrompt: subject.ontologyClassifyPrompt || '',
      ontologyDefinitionPrompt: subject.ontologyDefinitionPrompt || '',
      rerankingPrompt: subject.rerankingPrompt != null ? subject.rerankingPrompt : DEFAULT_RERANKING_PROMPT,
      promptRewritePrompt: subject.promptRewritePrompt != null ? subject.promptRewritePrompt : DEFAULT_PROMPT_REWRITE
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
    setSaving(true);
    setFormError('');
    const payload = {
      ...form,
      urlSlug: form.urlSlug ? slugify(form.urlSlug) : slugify(form.displayName),
      thinkingEnabled: !!form.thinkingEnabled,
      historyRetentionDays: Math.max(1, Number(form.historyRetentionDays) || 90)
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
          <div className="form-group" title={t('subjects.displayNameTip', 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content ingested is scoped to it. Required.')}>
            <label htmlFor="cd-name" title={t('subjects.displayNameTip', 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content ingested is scoped to it. Required.')}>
              {t('subjects.displayName')} <span className="required-mark">*</span>
            </label>
            <input
              id="cd-name"
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              required
              title={t('subjects.displayNameTip', 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content ingested is scoped to it. Required.')}
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
          <div className="form-group" title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages by relevance before answering. Leave as None to skip reranking.')}>
            <label htmlFor="cd-rerankmodel" title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages before answering. None skips reranking.')}>{t('subjects.rerankingModel', 'Reranking Model (optional)')}</label>
            <select id="cd-rerankmodel" value={form.rerankingModel} onChange={(e) => setForm({ ...form, rerankingModel: e.target.value })}
              title={t('subjects.rerankingModelTip', 'Optional completion endpoint used to re-rank retrieved passages before answering. None skips reranking.')}>
              <option value="">{t('subjects.none', 'None')}</option>
              {completionEndpoints.map((ep) => <option key={ep.id} value={ep.id}>{endpointLabel(ep)}</option>)}
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
          <div className="form-group" title={t('subjects.systemPromptTip', 'Appended after the global system prompt for every chat about this subject (global base + subject appended). A sensible default is supplied; edit or clear it to taste.')}>
            <label htmlFor="cd-sysprompt" title={t('subjects.systemPromptTip', 'Appended after the global system prompt for every chat about this subject (global base + subject appended). A sensible default is supplied; edit or clear it to taste.')}>{t('subjects.systemPrompt', 'System Prompt')}</label>
            <textarea
              id="cd-sysprompt"
              rows={3}
              value={form.systemPrompt}
              placeholder={t('subjects.systemPromptHint', 'Appended after the global system prompt for chats about this subject.')}
              onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
              title={t('subjects.systemPromptTip', 'Appended after the global system prompt for every chat about this subject (global base + subject appended). A sensible default is supplied; edit or clear it to taste.')}
            />
          </div>
          <div className="form-group" title={t('subjects.rerankingPromptTip', 'Used only when a reranking model is set. Appended after the global reranking prompt to guide how passages are ordered by relevance.')}>
            <label htmlFor="cd-rerankprompt" title={t('subjects.rerankingPromptTip', 'Used only when a reranking model is set. Appended after the global reranking prompt.')}>{t('subjects.rerankingPrompt', 'Reranking Prompt')}</label>
            <textarea
              id="cd-rerankprompt"
              rows={3}
              value={form.rerankingPrompt}
              onChange={(e) => setForm({ ...form, rerankingPrompt: e.target.value })}
              title={t('subjects.rerankingPromptTip', 'Used only when a reranking model is set. Appended after the global reranking prompt.')}
            />
          </div>
          <div className="form-group" title={t('subjects.promptRewritePromptTip', 'Used only when a prompt-rewrite model is set. Appended after the global prompt-rewrite prompt to guide how the question is rewritten into a retrieval query.')}>
            <label htmlFor="cd-rewriteprompt" title={t('subjects.promptRewritePromptTip', 'Used only when a prompt-rewrite model is set. Appended after the global prompt-rewrite prompt.')}>{t('subjects.promptRewritePrompt', 'Prompt Rewrite Prompt')}</label>
            <textarea
              id="cd-rewriteprompt"
              rows={3}
              value={form.promptRewritePrompt}
              onChange={(e) => setForm({ ...form, promptRewritePrompt: e.target.value })}
              title={t('subjects.promptRewritePromptTip', 'Used only when a prompt-rewrite model is set. Appended after the global prompt-rewrite prompt.')}
            />
          </div>
          <div className="form-group" title={t('subjects.ontologyClassifyTip', 'Appended after the global ontology classification prompt during ingestion. A sensible default is supplied; edit or clear it to taste.')}>
            <label htmlFor="cd-ontclass" title={t('subjects.ontologyClassifyTip', 'Appended after the global ontology classification prompt during ingestion. A sensible default is supplied; edit or clear it to taste.')}>{t('subjects.ontologyClassifyPrompt', 'Ontology Classification Prompt')}</label>
            <textarea
              id="cd-ontclass"
              rows={3}
              value={form.ontologyClassifyPrompt}
              onChange={(e) => setForm({ ...form, ontologyClassifyPrompt: e.target.value })}
              title={t('subjects.ontologyClassifyTip', 'Appended after the global ontology classification prompt during ingestion. A sensible default is supplied; edit or clear it to taste.')}
            />
          </div>
          <div className="form-group" title={t('subjects.ontologyDefinitionTip', 'Appended after the global ontology definition when mapping atoms into the graph. A sensible default is supplied; edit or clear it to taste.')}>
            <label htmlFor="cd-ontdef" title={t('subjects.ontologyDefinitionTip', 'Appended after the global ontology definition when mapping atoms into the graph. A sensible default is supplied; edit or clear it to taste.')}>{t('subjects.ontologyDefinitionPrompt', 'Ontology Definition')}</label>
            <textarea
              id="cd-ontdef"
              rows={3}
              value={form.ontologyDefinitionPrompt}
              onChange={(e) => setForm({ ...form, ontologyDefinitionPrompt: e.target.value })}
              title={t('subjects.ontologyDefinitionTip', 'Appended after the global ontology definition when mapping atoms into the graph. A sensible default is supplied; edit or clear it to taste.')}
            />
          </div>
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
