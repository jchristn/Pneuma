import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import { formatDateTime } from '../i18n/formatters';

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
    { name: 'displayName', label: 'Display Name', required: true, tip: 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content you ingest is scoped to it.' },
    { name: 'type', label: 'Type', type: 'text', placeholder: 'Person', default: 'Person', tip: 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.' },
    { name: 'description', label: 'Description', type: 'textarea', rows: 3, tip: 'Optional notes shown in the subjects list to help operators tell similar subjects apart.' },
    { name: 'urlSlug', label: 'URL Slug', placeholder: 'Derived from display name', deriveFrom: 'displayName', derive: slugify, tip: 'URL-safe slug used to reach this subject in the user dashboard (must be unique within the tenant). Auto-derived from the display name.' },
    { name: 'thinkingEnabled', label: 'Show model thinking in chat', type: 'checkbox', tip: 'When on, model reasoning is shown in a collapsible section (with a Thinking-time statistic) for chats about this subject. Off hides it. Applies to all chats about this subject.' },
    { name: 'historyRetentionDays', label: 'History Retention (days)', type: 'number', default: 90, min: 1, tip: 'How many days chat-turn history is kept for this subject before pruning. Minimum 1.' },
    { name: 'systemPrompt', label: 'Subject System Prompt', type: 'textarea', rows: 4, tip: 'Appended after the global system prompt for every chat about this subject (global base + subject appended). Leave blank to use the global prompt only.' },
    { name: 'ontologyClassifyPrompt', label: 'Subject Ontology Classification Prompt', type: 'textarea', rows: 4, tip: 'Appended after the global ontology classification prompt during ingestion. Leave blank to use the global prompt only.' },
    { name: 'ontologyDefinitionPrompt', label: 'Subject Ontology Definition', type: 'textarea', rows: 4, tip: 'Appended after the global ontology definition when mapping atoms into the graph. Leave blank to use the global definition only.' },
    { name: 'graphRootNodeId', label: 'Graph Root Node ID', placeholder: 'Derived from display name', deriveFrom: 'displayName', derive: slugify, tip: 'The knowledge-graph root node id for this subject. Auto-derived from the display name; override only if you need a specific slug.' }
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
      postDeleteNotice={t('subjects.deletingBackground', 'We are deleting this subject and everything associated with it in the background. You may close this window.')}
      extraActions={[
        { key: 'viewLinks', label: t('subjects.viewLinks'), onClick: (item) => navigate(`/dashboard/links?subjectId=${encodeURIComponent(item.id)}`) }
      ]}
    />
  );
}

export default SubjectsView;
