import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';

function PermissionsView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'resource', label: 'Resource', render: (r) => r.resource || '—' },
    { key: 'action', label: 'Action', render: (r) => r.action || '—' },
    { key: 'description', label: 'Description', cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={14} /> }
  ];
  const formFields = [
    { name: 'name', label: 'Name', required: true },
    { name: 'resource', label: 'Resource' },
    { name: 'action', label: 'Action' },
    { name: 'description', label: 'Description', type: 'textarea', rows: 3 }
  ];
  return (
    <ResourceView
      resourceKey="permissions"
      singular="permission"
      title={t('nav.permissions')}
      subtitle="Fine-grained permissions"
      columns={columns}
      formFields={formFields}
      idField="id"
    />
  );
}

export default PermissionsView;
