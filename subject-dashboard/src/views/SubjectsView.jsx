import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';


const EMPTY_FORM = {
  displayName: '', type: 'Subject', description: '', urlSlug: '',
  thinkingEnabled: false, historyRetentionDays: 90,
  systemPrompt: '', ontologyClassifyPrompt: '', ontologyDefinitionPrompt: ''
};

// Slug: lowercase, collapse non-alphanumeric runs to single dashes, trim dashes.
function slugify(value) {
  return String(value || '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

function SubjectsView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();

  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);
  const [deletingNotice, setDeletingNotice] = useState(false);

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.getSubjects({ maxResults: 1000 });
      setSubjects(asArray(res, 'subjects'));
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    setForm(EMPTY_FORM);
    setFormError('');
    setFormOpen(true);
  };

  const openEdit = (subject) => {
    setEditing(subject);
    setForm({
      displayName: subject.displayName || '',
      type: subject.type || 'Subject',
      description: subject.description || '',
      urlSlug: subject.urlSlug || '',
      thinkingEnabled: !!subject.thinkingEnabled,
      historyRetentionDays: subject.historyRetentionDays || 90,
      systemPrompt: subject.systemPrompt || '',
      ontologyClassifyPrompt: subject.ontologyClassifyPrompt || '',
      ontologyDefinitionPrompt: subject.ontologyDefinitionPrompt || ''
    });
    setFormError('');
    setFormOpen(true);
  };

  const handleSave = async (e) => {
    e.preventDefault();
    if (!form.displayName.trim()) {
      setFormError('Display name is required.');
      return;
    }
    setSaving(true);
    setFormError('');
    const payload = {
      ...form,
      urlSlug: form.urlSlug ? slugify(form.urlSlug) : slugify(form.displayName),
      thinkingEnabled: !!form.thinkingEnabled,
      historyRetentionDays: Math.max(1, Number(form.historyRetentionDays) || 90)
    };
    try {
      if (editing) {
        await apiClient.updateSubject(editing.id, payload);
      } else {
        await apiClient.createSubject(payload);
      }
      setFormOpen(false);
      await load();
    } catch (err) {
      setFormError(err.message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await apiClient.deleteSubject(deleteTarget.id);
      setDeleteTarget(null);
      setDeletingNotice(true);
      await load();
    } catch (err) {
      setError(err.message);
    } finally {
      setDeleting(false);
    }
  };

  const columns = [
    {
      key: 'displayName',
      label: t('subjects.displayName'),
      render: (v, row) => (row.deletionStatus && row.deletionStatus !== 'None')
        ? <span style={{ opacity: 0.5 }}><strong>{v || '(unnamed)'}</strong> · <em>{row.deletionStatus === 'Failed' ? t('subjects.deletionFailed', 'deletion failed') : t('subjects.deleting', 'deleting…')}</em></span>
        : <strong>{v || '(unnamed)'}</strong>
    },
    { key: 'type', label: t('subjects.type') },
    {
      key: 'description',
      label: t('subjects.description'),
      sortable: false,
      render: (v) => <span title={v}>{v || '—'}</span>
    },
    {
      key: 'id',
      label: 'ID',
      className: 'cell-id',
      sortable: false,
      render: (v) => <CopyableId value={v} title="Copy subject ID" />
    },
    {
      key: '_actions',
      label: t('common.actions'),
      className: 'actions-column',
      sortable: false,
      render: (_v, row) => (
        <ActionMenu
          actions={[
            { label: t('common.edit'), onClick: () => openEdit(row) },
            { label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(row) }
          ]}
        />
      )
    }
  ];

  return (
    <div>
      <PageHeader
        title={t('subjects.title')}
        subtitle={t('subjects.subtitle')}
        actions={
          <button className="btn btn-primary" onClick={openCreate}>
            {t('subjects.add')}
          </button>
        }
      />

      {error && <div className="error-banner">{error}</div>}

      <DataTable
        columns={columns}
        data={subjects}
        loading={loading}
        onRefresh={load}
        emptyTitle={t('subjects.title')}
        emptyDescription={t('subjects.empty')}
      />

      <Modal
        isOpen={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? t('subjects.editTitle') : t('subjects.createTitle')}
        size="medium"
      >
        <form onSubmit={handleSave}>
          {formError && <div className="form-error">{formError}</div>}
          <div className="form-group">
            <label htmlFor="cd-name">
              {t('subjects.displayName')} <span className="required-mark">*</span>
            </label>
            <input
              id="cd-name"
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              required
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-type">{t('subjects.type')}</label>
            <input
              id="cd-type"
              type="text"
              value={form.type}
              placeholder="Subject"
              onChange={(e) => setForm({ ...form, type: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-desc">{t('subjects.description')}</label>
            <textarea
              id="cd-desc"
              rows={4}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-slug">{t('subjects.urlSlug', 'URL Slug')}</label>
            <input
              id="cd-slug"
              type="text"
              value={form.urlSlug}
              placeholder={slugify(form.displayName) || 'derived-from-name'}
              onChange={(e) => setForm({ ...form, urlSlug: e.target.value })}
            />
            <div className="field-hint">{t('subjects.urlSlugHint', 'URL-safe slug used to reach this subject in the user dashboard. Must be unique.')}</div>
          </div>
          <div className="form-group" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <input
              id="cd-thinking"
              type="checkbox"
              style={{ width: 'auto' }}
              checked={form.thinkingEnabled}
              onChange={(e) => setForm({ ...form, thinkingEnabled: e.target.checked })}
            />
            <label htmlFor="cd-thinking" style={{ margin: 0 }}>{t('subjects.thinkingEnabled', 'Show model thinking in chat')}</label>
          </div>
          <div className="form-group">
            <label htmlFor="cd-retention">{t('subjects.historyRetentionDays', 'History Retention (days)')}</label>
            <input
              id="cd-retention"
              type="number"
              min={1}
              value={form.historyRetentionDays}
              onChange={(e) => setForm({ ...form, historyRetentionDays: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-sysprompt">{t('subjects.systemPrompt', 'Subject System Prompt')}</label>
            <textarea
              id="cd-sysprompt"
              rows={3}
              value={form.systemPrompt}
              placeholder={t('subjects.systemPromptHint', 'Appended after the global system prompt for chats about this subject.')}
              onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-ontclass">{t('subjects.ontologyClassifyPrompt', 'Subject Ontology Classification Prompt')}</label>
            <textarea
              id="cd-ontclass"
              rows={3}
              value={form.ontologyClassifyPrompt}
              onChange={(e) => setForm({ ...form, ontologyClassifyPrompt: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label htmlFor="cd-ontdef">{t('subjects.ontologyDefinitionPrompt', 'Subject Ontology Definition')}</label>
            <textarea
              id="cd-ontdef"
              rows={3}
              value={form.ontologyDefinitionPrompt}
              onChange={(e) => setForm({ ...form, ontologyDefinitionPrompt: e.target.value })}
            />
          </div>
          <div className="form-actions">
            <button type="button" className="btn btn-secondary" onClick={() => setFormOpen(false)} disabled={saving}>
              {t('common.cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? t('common.loading') : editing ? t('common.save') : t('common.create')}
            </button>
          </div>
        </form>
      </Modal>

      <ConfirmModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('common.delete')}
        message={`Delete this subject and everything associated with it? This cannot be undone.`}
        entityName={deleteTarget?.displayName}
        confirmLabel={t('common.delete')}
        isLoading={deleting}
      />

      <Modal
        isOpen={deletingNotice}
        onClose={() => setDeletingNotice(false)}
        title={t('subjects.deletingTitle', 'Deleting in the background')}
      >
        <p>{t('subjects.deletingBackground', 'We are deleting this subject and everything associated with it in the background. You may close this window.')}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-primary" onClick={() => setDeletingNotice(false)}>{t('common.close')}</button>
        </div>
      </Modal>
    </div>
  );
}

export default SubjectsView;
