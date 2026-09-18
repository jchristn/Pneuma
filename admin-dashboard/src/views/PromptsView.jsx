import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import ResourceView from '../components/ResourceView';
import PageHeader from '../components/PageHeader';
import ConfirmModal from '../components/ConfirmModal';
import CopyableId from '../components/CopyableId';
import ErrorBanner from '../components/ErrorBanner';

// Segmented Global / Subject scope switch, reused as the header action in both scopes.
function ScopeSwitch({ scope, onChange }) {
  const { t } = useTranslation();
  return (
    <div className="segmented" role="tablist" aria-label={t('prompts.scope')}>
      <button type="button" role="tab" aria-selected={scope === 'global'} className={scope === 'global' ? 'active' : ''}
        onClick={() => onChange('global')} title={t('prompts.scopeGlobalTip')}>
        {t('prompts.scopeGlobal')}
      </button>
      <button type="button" role="tab" aria-selected={scope === 'subject'} className={scope === 'subject' ? 'active' : ''}
        onClick={() => onChange('subject')} title={t('prompts.scopeSubjectTip')}>
        {t('prompts.scopeSubject')}
      </button>
    </div>
  );
}

// One prompt key for a subject: shows the effective content, its source, the global fallback, and an editor
// to set / clear a subject-level override.
function SubjectPromptCard({ prompt, subjectId, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const hasOverride = prompt.source === 'SubjectOverride' || prompt.overrideContent != null;
  const [content, setContent] = useState(prompt.overrideContent || '');
  const [mergeMode, setMergeMode] = useState(prompt.mergeMode || 'Append');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [confirmReset, setConfirmReset] = useState(false);

  // Re-sync local editor state whenever the underlying prompt is reloaded.
  useEffect(() => {
    setContent(prompt.overrideContent || '');
    setMergeMode(prompt.mergeMode || 'Append');
  }, [prompt.overrideContent, prompt.mergeMode, prompt.key]);

  const save = async () => {
    setSaving(true);
    setError('');
    try {
      await apiClient.updateSubjectPrompt(subjectId, prompt.key, { content, mergeMode });
      await onChanged();
    } catch (err) {
      setError(err?.message || 'Save failed');
    } finally {
      setSaving(false);
    }
  };

  const reset = async () => {
    await apiClient.deleteSubjectPrompt(subjectId, prompt.key);
    await onChanged();
  };

  return (
    <div className="prompt-card">
      <div className="prompt-card-head">
        <code className="cell-id">{prompt.key}</code>
        {prompt.name && <span className="prompt-name">{prompt.name}</span>}
        <span className={`pill ${hasOverride ? 'pill-info' : 'pill-neutral'}`}>
          {hasOverride ? t('prompts.sourceOverride') : t('prompts.sourceGlobal')}
        </span>
        {hasOverride && (
          <span className="pill pill-neutral" title={t('prompts.mergeModeTip')}>
            {mergeMode === 'Replace' ? t('prompts.mergeReplace') : t('prompts.mergeAppend')}
          </span>
        )}
      </div>

      <div className="prompt-field">
        <label>{t('prompts.effective')}</label>
        <div className="prompt-readonly">{prompt.effectiveContent || '—'}</div>
      </div>

      <div className="prompt-field">
        <label>{t('prompts.globalDefault')}</label>
        <div className="prompt-readonly prompt-muted">{prompt.globalContent || '—'}</div>
        {!hasOverride && <small className="field-hint">{t('prompts.inheritsGlobal')}</small>}
      </div>

      <div className="prompt-field">
        <label htmlFor={`ov-${prompt.key}`}>{t('prompts.override')}</label>
        <textarea id={`ov-${prompt.key}`} rows={5} value={content} placeholder={t('prompts.overridePlaceholder')}
          onChange={(e) => setContent(e.target.value)} />
        <small className="field-hint">{t('prompts.overrideHint')}</small>
      </div>

      <div className="prompt-editor-row">
        <div className="field">
          <label htmlFor={`mm-${prompt.key}`}>{t('prompts.mergeMode')}</label>
          <select id={`mm-${prompt.key}`} value={mergeMode} onChange={(e) => setMergeMode(e.target.value)}
            title={t('prompts.mergeModeTip')}>
            <option value="Append">{t('prompts.mergeAppend')}</option>
            <option value="Replace">{t('prompts.mergeReplace')}</option>
          </select>
        </div>
        <div className="prompt-card-actions">
          <button type="button" className="button-primary" onClick={save} disabled={saving}>
            {saving ? t('common.loading') : t('common.save')}
          </button>
          <button type="button" className="button-danger" onClick={() => setConfirmReset(true)} disabled={saving || !hasOverride}
            title={t('prompts.resetTip')}>
            {t('prompts.resetToGlobal')}
          </button>
        </div>
      </div>

      {error && <div className="error-message" style={{ marginTop: '0.5rem' }}>{error}</div>}

      {confirmReset && (
        <ConfirmModal
          title={t('prompts.resetToGlobal')}
          message={t('prompts.resetConfirm', { key: prompt.key })}
          confirmLabel={t('prompts.resetToGlobal')}
          onConfirm={reset}
          onClose={() => setConfirmReset(false)}
        />
      )}
    </div>
  );
}

// Subject scope: pick a subject, then list every prompt key with its override editor.
function SubjectPrompts({ headerActions }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [prompts, setPrompts] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    apiClient.list('subjects', { maxResults: 1000 })
      .then((r) => setSubjects(normalizeList(r).items))
      .catch(() => {});
  }, [apiClient]);

  const load = useCallback(async () => {
    if (!subjectId) { setPrompts([]); return; }
    setLoading(true);
    setError('');
    try {
      const resp = await apiClient.getSubjectPrompts(subjectId);
      setPrompts(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load prompts');
      setPrompts([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);

  return (
    <div>
      <PageHeader title={t('prompts.title')} subtitle={t('prompts.subjectSubtitle')} actions={headerActions} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="prompt-subject">{t('prompts.subject')}</label>
          <select id="prompt-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('prompts.selectSubject')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
          </select>
        </div>
        {subjectId && <button type="button" className="button-secondary" onClick={load} disabled={loading}>{t('common.refresh')}</button>}
      </div>

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError('')} />}

      {!subjectId && <div className="empty-message">{t('prompts.pickSubject')}</div>}
      {subjectId && !loading && prompts.length === 0 && !error && (
        <div className="empty-message">{t('prompts.noneForSubject')}</div>
      )}
      {subjectId && (
        <div className="prompt-list">
          {prompts.map((p) => (
            <SubjectPromptCard key={p.key} prompt={p} subjectId={subjectId} onChanged={load} />
          ))}
        </div>
      )}
    </div>
  );
}

function PromptsView() {
  const { t } = useTranslation();
  const [scope, setScope] = useState('global');
  const scopeSwitch = <ScopeSwitch scope={scope} onChange={setScope} />;

  if (scope === 'subject') {
    return <SubjectPrompts headerActions={scopeSwitch} />;
  }

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
    { name: 'key', label: 'Key', placeholder: 'ontology.classify', required: true, tip: 'The stable identifier the pipeline looks this prompt up by (e.g. "assistant.system", "user.answer"). Must match what the code expects.' },
    { name: 'description', label: 'Description', tip: 'A short note on what this prompt controls, for other operators.' },
    { name: 'content', label: t('prompts.content'), type: 'textarea', rows: 12, tip: 'The prompt text sent to the model. Edit to tune tone, guardrails, and citation behavior. Changes take effect on the next request.' }
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
      duplicable
      duplicateTransform={(r) => ({ ...r, key: r.key ? `${r.key}.copy` : '' })}
      headerActions={scopeSwitch}
    />
  );
}

export default PromptsView;
