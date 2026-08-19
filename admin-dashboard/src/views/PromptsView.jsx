import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';

function PromptsView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'key', label: 'Key', render: (r) => <code className="cell-id">{r.key || r.name || '—'}</code> },
    { key: 'description', label: 'Description', cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'content', label: 'Content', cellClass: 'wrap', sortable: false, render: (r) => {
      const c = r.content || r.text || '';
      return c.length > 80 ? `${c.slice(0, 80)}…` : (c || '—');
    } },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={12} /> }
  ];
  const formFields = [
    { name: 'key', label: 'Key', placeholder: 'ontology.classify', required: true },
    { name: 'description', label: 'Description' },
    { name: 'content', label: t('prompts.content'), type: 'textarea', rows: 12 }
  ];
  return (
    <ResourceView
      resourceKey="prompts"
      singular="prompt"
      title={t('prompts.title')}
      subtitle={t('prompts.subtitle')}
      columns={columns}
      formFields={formFields}
      idField="id"
      modalSize="prompt"
    />
  );
}

export default PromptsView;
