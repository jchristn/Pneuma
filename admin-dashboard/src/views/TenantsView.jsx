import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import { formatDateTime } from '../i18n/formatters';

function TenantsView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={14} /> },
    { key: 'active', label: 'Active', render: (r) => <StatusPill label={r.active === false ? 'Disabled' : 'Active'} tone={r.active === false ? 'neutral' : 'success'} /> },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc || r.CreatedUtc) }
  ];
  const formFields = [
    { name: 'name', label: 'Name', required: true },
    { name: 'active', label: 'Active', type: 'checkbox', default: true, omitIfEmpty: false }
  ];
  return (
    <ResourceView
      resourceKey="tenants"
      singular="tenant"
      title={t('nav.tenants')}
      subtitle="Manage tenants"
      columns={columns}
      formFields={formFields}
      idField="id"
    />
  );
}

export default TenantsView;
