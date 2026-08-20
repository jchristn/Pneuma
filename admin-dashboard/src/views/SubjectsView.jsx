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
    { key: 'displayName', label: 'Display Name', render: (r) => r.displayName || r.name || '—' },
    { key: 'type', label: 'Type', render: (r) => <StatusPill label={r.type} tone="info" /> },
    { key: 'description', label: 'Description', cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'graphRootNodeId', label: 'Graph Root Node', render: (r) => <CopyableId value={r.graphRootNodeId} truncateLen={12} /> },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc || r.CreatedUtc) }
  ];
  const formFields = [
    { name: 'displayName', label: 'Display Name', required: true, tip: 'The name of the subject this archive is about (e.g. "Ada Lovelace"). All content you ingest is scoped to it.' },
    { name: 'type', label: 'Type', type: 'text', placeholder: 'Person', default: 'Person', tip: 'A free-form category (Person, Product, Topic…). Descriptive only — it does not restrict what you can ingest.' },
    { name: 'description', label: 'Description', type: 'textarea', rows: 3, tip: 'Optional notes shown in the subjects list to help operators tell similar subjects apart.' },
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
      extraActions={[
        { key: 'viewLinks', label: t('subjects.viewLinks'), onClick: (item) => navigate(`/dashboard/links?subjectId=${encodeURIComponent(item.id)}`) }
      ]}
    />
  );
}

export default SubjectsView;
