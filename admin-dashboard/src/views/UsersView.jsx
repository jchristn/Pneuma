import { useEffect, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import ResourceView from '../components/ResourceView';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import { formatDateTime } from '../i18n/formatters';

function UsersView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [tenants, setTenants] = useState([]);

  // Load tenants once so the table can show tenant names instead of raw ids.
  useEffect(() => {
    let cancelled = false;
    apiClient.list('tenants', { maxResults: 1000 })
      .then((resp) => { if (!cancelled) setTenants(normalizeList(resp).items); })
      .catch(() => { if (!cancelled) setTenants([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  const tenantName = useCallback((id) => {
    if (!id) return null;
    const match = tenants.find((tenant) => (tenant.id ?? tenant.guid) === id);
    return match ? (match.name || match.displayName || id) : id;
  }, [tenants]);

  const columns = [
    { key: 'email', label: 'Email', render: (r) => r.email || '—' },
    { key: 'firstName', label: 'First Name', render: (r) => r.firstName || '—' },
    { key: 'lastName', label: 'Last Name', render: (r) => r.lastName || '—' },
    { key: 'isAdmin', label: 'Role', render: (r) => (
      r.isAdmin ? <StatusPill label="Admin" tone="info" />
        : r.isTenantAdmin ? <StatusPill label="Tenant Admin" tone="warning" />
          : <StatusPill label="User" tone="neutral" />
    ) },
    { key: 'tenantId', label: 'Tenant', render: (r) => (
      r.tenantId ? <span title={r.tenantId}>{tenantName(r.tenantId)}</span> : '—'
    ) },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc || r.CreatedUtc) }
  ];
  const formFields = [
    { name: 'firstName', label: 'First Name', required: true },
    { name: 'lastName', label: 'Last Name', required: true },
    { name: 'email', label: 'Email', type: 'email', required: true },
    { name: 'password', label: 'Password (leave blank to keep)', type: 'password' },
    { name: 'tenantId', label: 'Tenant ID (optional)' },
    { name: 'isAdmin', label: 'System Admin', type: 'checkbox', omitIfEmpty: false },
    { name: 'isTenantAdmin', label: 'Tenant Admin', type: 'checkbox', omitIfEmpty: false }
  ];
  const detailFields = formFields
    .filter((f) => f.name !== 'password')
    .map((f) => (f.name === 'tenantId'
      ? { ...f, render: (item) => (item.tenantId ? <CopyableId value={item.tenantId} /> : '—') }
      : f));
  return (
    <ResourceView
      resourceKey="users"
      singular="user"
      title={t('nav.users')}
      subtitle="Manage users across tenants"
      columns={columns}
      formFields={formFields}
      detailFields={detailFields}
      idField="id"
    />
  );
}

export default UsersView;
