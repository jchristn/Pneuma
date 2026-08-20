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
    { name: 'name', label: 'Name', required: true, tip: 'A label to recognize this API key by (e.g. "ci-pipeline"). The access/secret pair is generated on creation.' },
    { name: 'userId', label: 'User ID (optional)', tip: 'Bind the credential to a specific user so its actions inherit that user’s permissions. Leave blank for a tenant-scoped key.' },
    { name: 'expiresUtc', label: 'Expires UTC (optional)', placeholder: 'YYYY-MM-DDTHH:mm:ssZ', tip: 'When the key stops working (ISO-8601 UTC). Leave blank for a non-expiring key.' }
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
