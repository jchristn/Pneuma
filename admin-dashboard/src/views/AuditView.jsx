import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import { formatDateTime } from '../i18n/formatters';

function AuditView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'timestampUtc', label: 'Time', render: (r) => formatDateTime(r.timestampUtc || r.createdUtc || r.time || r.CreatedUtc) },
    { key: 'eventType', label: 'Event', render: (r) => <StatusPill label={r.eventType || r.type || r.action || 'event'} tone={toneForStatus(r.eventType || r.type)} /> },
    { key: 'message', label: 'Message', cellClass: 'wrap', sortable: false, render: (r) => r.message || r.description || '—' },
    { key: 'principal', label: 'Principal', render: (r) => r.principalName || r.principal || r.userId || '—' },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={12} /> }
  ];
  return (
    <ResourceView
      resourceKey="audit"
      singular="audit event"
      title={t('nav.audit')}
      subtitle="Recent security and RBAC events"
      columns={columns}
      formFields={[]}
      capabilities={{ create: false, edit: false, delete: false, viewJson: true }}
      fetcher={(apiClient) => apiClient.listAudit({ maxResults: 200, order: 'desc' })}
      idField="id"
    />
  );
}

export default AuditView;
