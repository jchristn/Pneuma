import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';

function RolesView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'description', label: 'Description', cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'isBuiltIn', label: 'Built-in', render: (r) => (r.isBuiltIn || r.builtIn) ? <StatusPill label="Built-in" tone="info" /> : <StatusPill label="Custom" tone="neutral" /> },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={14} /> }
  ];
  const formFields = [
    { name: 'name', label: 'Name', required: true },
    { name: 'description', label: 'Description', type: 'textarea', rows: 3 }
  ];
  return (
    <ResourceView
      resourceKey="roles"
      singular="role"
      title={t('nav.roles')}
      subtitle="Role-based access control roles"
      columns={columns}
      formFields={formFields}
      idField="id"
    />
  );
}

export default RolesView;
