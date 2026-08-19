import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewer from '../components/JsonViewer';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import { getId } from '../components/ResourceView';

function AssignmentsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [userId, setUserId] = useState('');
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState(null);
  const [modal, setModal] = useState(null);
  const [roleId, setRoleId] = useState('');

  const load = useCallback(async () => {
    if (!userId) return;
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.listAssignments(userId);
      setRows(normalizeList(resp).items);
      setLoaded(true);
    } catch (err) {
      setError(err?.message || 'Failed to load assignments');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, userId]);

  const create = async () => {
    await apiClient.create('assignments', { userId, roleId });
    setModal(null);
    setRoleId('');
    await load();
  };

  const remove = async (item) => {
    await apiClient.remove('assignments', getId(item));
    await load();
  };

  const columns = [
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id ?? r.guid} truncateLen={14} /> },
    { key: 'roleId', label: 'Role', render: (r) => <CopyableId value={r.roleId ?? r.roleName} truncateLen={14} /> },
    { key: 'userId', label: 'User', render: (r) => <CopyableId value={r.userId} truncateLen={14} /> },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (item) => (
      <ActionMenu items={[
        { key: 'json', label: t('common.viewJson'), onClick: () => setModal({ type: 'json', item }) },
        { key: 'delete', label: t('common.delete'), danger: true, onClick: () => setModal({ type: 'delete', item }) }
      ]} />
    ) }
  ];

  return (
    <div>
      <PageHeader
        title={t('nav.assignments')}
        subtitle="Role assignments require a user ID"
        actions={(
          <button type="button" className="button-primary" disabled={!userId} onClick={() => setModal({ type: 'create' })}>
            + {t('common.add')}
          </button>
        )}
      />
      <div className="filter-bar">
        <div className="field" style={{ minWidth: '280px' }}>
          <label htmlFor="assign-user">User ID</label>
          <input id="assign-user" value={userId} onChange={(e) => setUserId(e.target.value)}
            placeholder="Enter user ID" onKeyDown={(e) => e.key === 'Enter' && load()} />
        </div>
        <button type="button" className="button-primary" onClick={load} disabled={!userId || loading}>{t('common.search')}</button>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      {loaded ? (
        <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} />
      ) : (
        <div className="section"><p style={{ color: 'var(--color-text-secondary)' }}>Enter a user ID and search to view role assignments.</p></div>
      )}

      {modal?.type === 'create' && (
        <Modal title="Create assignment" size="sm" onClose={() => setModal(null)}
          footer={(
            <>
              <button type="button" className="button-secondary" onClick={() => setModal(null)}>{t('common.cancel')}</button>
              <button type="button" className="button-primary" disabled={!roleId} onClick={create}>{t('common.create')}</button>
            </>
          )}>
          <div className="form-grid">
            <div className="field"><label>User ID</label><input value={userId} disabled /></div>
            <div className="field"><label htmlFor="roleId">Role ID</label><input id="roleId" value={roleId} onChange={(e) => setRoleId(e.target.value)} placeholder="Enter role ID" /></div>
          </div>
        </Modal>
      )}
      {modal?.type === 'json' && <JsonViewer data={modal.item} onClose={() => setModal(null)} />}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('common.delete')} message={t('resource.deleteConfirm', { name: 'assignment' })}
          confirmLabel={t('common.delete')} onConfirm={() => remove(modal.item)} onClose={() => setModal(null)} />
      )}
    </div>
  );
}

export default AssignmentsView;
