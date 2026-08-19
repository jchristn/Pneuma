import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import { formatDateTime } from '../i18n/formatters';

function CredentialsView() {
  const { t } = useTranslation();
  const columns = [
    { key: 'name', label: 'Name', render: (r) => r.name || '—' },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={14} /> },
    { key: 'userId', label: 'User', render: (r) => <CopyableId value={r.userId} truncateLen={12} /> },
    { key: 'accessKey', label: 'Access Key', render: (r) => <CopyableId value={r.accessKey} truncateLen={14} /> },
    { key: 'expiresUtc', label: 'Expires', render: (r) => formatDateTime(r.expiresUtc) },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc || r.CreatedUtc) }
  ];
  const formFields = [
    { name: 'name', label: 'Name', required: true },
    { name: 'userId', label: 'User ID (optional)' },
    { name: 'expiresUtc', label: 'Expires UTC (optional)', placeholder: 'YYYY-MM-DDTHH:mm:ssZ' }
  ];
  // Credential create returns a one-time secretKey; show the full response so the
  // operator can copy it before it is gone.
  const onCreated = (resp, setModal) => {
    if (resp && (resp.secretKey || resp.SecretKey)) {
      setModal({ type: 'json', item: resp });
    }
  };
  return (
    <ResourceView
      resourceKey="credentials"
      singular="credential"
      title={t('nav.credentials')}
      subtitle="API keys — the secret is shown only once at creation"
      columns={columns}
      formFields={formFields}
      capabilities={{ create: true, edit: false, delete: true, viewJson: true }}
      onCreated={onCreated}
      idField="id"
    />
  );
}

export default CredentialsView;
