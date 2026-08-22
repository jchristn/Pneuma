import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import ResourceView from '../components/ResourceView';
import FacetFilterEditor from '../components/FacetFilterEditor';
import StatusPill from '../components/StatusPill';
import { formatDateTime } from '../i18n/formatters';

// { value, label } options from an ingestion-endpoint entry.
function endpointOptions(list) {
  return (Array.isArray(list) ? list : []).map((e) => ({ value: e.id ?? e.Id, label: e.name || e.model || e.id || e.Id }));
}

// { value, label } options from a vector-collection entry (name + dimensionality).
function collectionSelectOptions(list) {
  return (Array.isArray(list) ? list : []).map((c) => {
    const dims = c.dimensionality ?? c.Dimensionality;
    const name = c.name || c.Name || c.id || c.Id;
    return { value: c.id ?? c.Id, label: dims ? `${name} (${dims}d)` : name };
  });
}

// Default subject-level rerank/rewrite prompts, mirroring the backend Subject defaults.
const DEFAULT_RERANKING_PROMPT = 'Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.';
const DEFAULT_PROMPT_REWRITE = 'Rewrite the question into a single, self-contained search query for this subject’s archive: resolve references, expand abbreviations, and keep it concise.';

// Sensible starter prompts pre-filled when creating a subject. They are appended after the global prompts,
// so they refine (not replace) the platform defaults; operators can edit or clear them.
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

// Slug: lowercase, collapse non-alphanumeric runs to single dashes, trim dashes.
export function slugify(value) {
  return String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function SubjectsView() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { apiClient } = useAuth();
  const [embeddingOptions, setEmbeddingOptions] = useState([]);
  const [completionOptions, setCompletionOptions] = useState([]);
  const [collectionOptions, setCollectionOptions] = useState([]);

  useEffect(() => {
    let cancelled = false;
    apiClient.listIngestionEndpoints()
      .then((resp) => {
        if (cancelled) return;
        setEmbeddingOptions(endpointOptions(resp?.embedding || resp?.Embedding));
        setCompletionOptions(endpointOptions(resp?.completion || resp?.Completion));
      })
      .catch(() => { if (!cancelled) { setEmbeddingOptions([]); setCompletionOptions([]); } });
    apiClient.listCollections()
      .then((resp) => { if (!cancelled) setCollectionOptions(collectionSelectOptions(normalizeList(resp).items)); })
      .catch(() => { if (!cancelled) setCollectionOptions([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  const soleDefault = (opts) => (opts.length === 1 ? opts[0].value : undefined);

  const columns = [
    { key: 'displayName', label: 'Display Name', render: (r) => (
      (r.deletionStatus && r.deletionStatus !== 'None')
        ? <span style={{ opacity: 0.5 }}>{r.displayName || r.name || '—'} · <em>{r.deletionStatus === 'Failed' ? 'deletion failed' : 'deleting…'}</em></span>
        : (r.displayName || r.name || '—')
    ) },
    { key: 'type', label: 'Type', render: (r) => <StatusPill label={r.type} tone="info" /> },
    { key: 'description', label: 'Description', cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'urlSlug', label: 'Slug', render: (r) => r.urlSlug ? <code className="cell-id">{r.urlSlug}</code> : '—' },
    { key: 'thinkingEnabled', label: 'Thinking', sortable: false, render: (r) => <StatusPill label={r.thinkingEnabled ? 'On' : 'Off'} tone={r.thinkingEnabled ? 'success' : 'neutral'} /> },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc || r.CreatedUtc) }
  ];
  const formFields = [
    // Line 1
    { name: 'displayName', label: 'Display Name', required: true, tip: 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content you ingest is scoped to it.' },
    { name: 'type', label: 'Type', type: 'text', placeholder: 'Person', default: 'Person', tip: 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.' },
    // Line 2
    { name: 'urlSlug', label: 'URL Slug', placeholder: 'Derived from display name', deriveFrom: 'displayName', derive: slugify, tip: 'URL-safe slug used to reach this subject in the user dashboard (must be unique within the tenant). Auto-derived from the display name.' },
    { name: 'historyRetentionDays', label: 'Chat History Retention (days)', type: 'number', default: 90, min: 1, tip: 'How many days chat-turn history is kept for this subject before pruning. Minimum 1.' },
    // Line 3
    { name: 'graphRootNodeId', label: 'Graph Root Node ID', placeholder: 'Derived from display name', deriveFrom: 'displayName', derive: slugify, tip: 'The knowledge-graph root node id for this subject. Auto-derived from the display name; override only if you need a specific slug.' },
    { name: 'thinkingEnabled', label: 'Show Thinking', type: 'checkbox', tip: 'When on, model reasoning is shown in a collapsible section (with a Thinking-time statistic) for chats about this subject. Off hides it. Applies to all chats about this subject.' },
    // Models & collection — required to ingest links or answer questions about this subject.
    { name: 'embeddingModel', label: 'Embedding Model', type: 'select', required: true, placeholder: 'Select a model', options: embeddingOptions, default: soleDefault(embeddingOptions), tip: 'The embedding endpoint used to vectorize this subject’s content at ingestion and to embed queries when answering. Must match the collection’s dimensionality. Required to ingest links.' },
    { name: 'inferenceModel', label: 'Inference Model', type: 'select', required: true, placeholder: 'Select a model', options: completionOptions, default: soleDefault(completionOptions), tip: 'The completion endpoint used for this subject’s ingestion inference (classification/summarization) and answer generation. Required to ingest links or answer questions.' },
    { name: 'collection', label: 'Collection', type: 'select', required: true, placeholder: 'Select a collection', options: collectionOptions, default: soleDefault(collectionOptions), tip: 'The RecallDB collection where this subject’s chunks are stored and searched. Choose one whose dimensionality matches the embedding model. Required to ingest links.' },
    { name: 'rerankingModel', label: 'Reranking Model (optional)', type: 'select', placeholder: 'None', omitIfEmpty: false, options: completionOptions, tip: 'Optional completion endpoint used to re-rank retrieved passages by relevance before answering. Leave as None to skip reranking.' },
    { name: 'promptRewriteModel', label: 'Prompt Rewrite Model (optional)', type: 'select', placeholder: 'None', omitIfEmpty: false, options: completionOptions, tip: 'Optional completion endpoint used to rewrite the user’s question into a retrieval query before searching. Leave as None to skip prompt rewriting.' },
    // Full-width prompts
    { name: 'systemPrompt', label: 'System Prompt', type: 'textarea', rows: 4, fullWidth: true, default: DEFAULT_SYSTEM_PROMPT, tip: 'Appended after the global system prompt for every chat about this subject (global base + subject appended). A sensible default is supplied; edit or clear it to taste.' },
    { name: 'rerankingPrompt', label: 'Reranking Prompt', type: 'textarea', rows: 3, fullWidth: true, default: DEFAULT_RERANKING_PROMPT, tip: 'Used only when a reranking model is set. Appended after the global reranking prompt to guide how passages are ordered by relevance.' },
    { name: 'promptRewritePrompt', label: 'Prompt Rewrite Prompt', type: 'textarea', rows: 3, fullWidth: true, default: DEFAULT_PROMPT_REWRITE, tip: 'Used only when a prompt-rewrite model is set. Appended after the global prompt-rewrite prompt to guide how the question is rewritten into a retrieval query.' },
    { name: 'ontologyClassifyPrompt', label: 'Ontology Classification Prompt', type: 'textarea', rows: 4, fullWidth: true, default: DEFAULT_ONTOLOGY_CLASSIFY, tip: 'Appended after the global ontology classification prompt during ingestion. A sensible default is supplied; edit or clear it to taste.' },
    { name: 'ontologyDefinitionPrompt', label: 'Ontology Definition', type: 'textarea', rows: 4, fullWidth: true, default: DEFAULT_ONTOLOGY_DEFINITION, tip: 'Appended after the global ontology definition when mapping atoms into the graph. A sensible default is supplied; edit or clear it to taste.' },
    { name: 'retrievalFilterJson', label: 'Retrieval Filter (optional)', type: 'custom', fullWidth: true, tip: 'Optional default facet filter restricting which ingested chunks answers may draw on. Add required/excluded labels (e.g. html, pdf) and tag key/value pairs; a per-request filter narrows this further. Leave empty for no filter.', render: (val, set) => <FacetFilterEditor value={val} onChange={set} /> },
    { name: 'description', label: 'Description', type: 'textarea', rows: 3, fullWidth: true, tip: 'Optional notes shown in the subjects list to help operators tell similar subjects apart.' },
    { name: 'tagline', label: 'Ask-Page Tagline', type: 'textarea', rows: 2, fullWidth: true, default: DEFAULT_TAGLINE, tip: "The subtitle shown beneath this subject's name on its ask page in the user dashboard (under the search box before asking, and under the chat header after). A sensible default is supplied; edit it to set the tone for this subject." }
  ];
  return (
    <ResourceView
      resourceKey="subjects"
      singular="subject"
      title={t('nav.subjects')}
      subtitle="Manage subjects and their knowledge graphs"
      columns={columns}
      formFields={formFields}
      idField="id"
      modalSize="subject"
      twoColumnForm
      duplicable
      duplicateTransform={(r) => {
        const dn = `${r.displayName || r.name || 'Subject'} (copy)`;
        return { ...r, displayName: dn, urlSlug: slugify(dn), graphRootNodeId: slugify(dn) };
      }}
      postDeleteNotice={t('subjects.deletingBackground', 'We are deleting this subject and everything associated with it in the background. You may close this window.')}
      extraActions={[
        { key: 'viewLinks', label: t('subjects.viewLinks'), onClick: (item) => navigate(`/dashboard/links?subjectId=${encodeURIComponent(item.id)}`) }
      ]}
    />
  );
}

export default SubjectsView;
