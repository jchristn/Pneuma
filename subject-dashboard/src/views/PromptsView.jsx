import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ConfirmModal from '../components/ConfirmModal';

// Segmented Global / Subject scope switch, reused as the header action in both scopes.
function ScopeSwitch({ scope, onChange }) {
  const { t } = useTranslation();
  return (
    <div className="segmented" role="tablist" aria-label={t('prompts.scope')}>
      <button type="button" role="tab" aria-selected={scope === 'global'} className={`segmented-btn ${scope === 'global' ? 'active' : ''}`}
        onClick={() => onChange('global')} title={t('prompts.scopeGlobalTip')}>
        {t('prompts.scopeGlobal')}
      </button>
      <button type="button" role="tab" aria-selected={scope === 'subject'} className={`segmented-btn ${scope === 'subject' ? 'active' : ''}`}
        onClick={() => onChange('subject')} title={t('prompts.scopeSubjectTip')}>
        {t('prompts.scopeSubject')}
      </button>
    </div>
  );
}

// A single global prompt with an inline content editor.
function GlobalPromptCard({ prompt, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [content, setContent] = useState(prompt.content || prompt.text || '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => { setContent(prompt.content || prompt.text || ''); }, [prompt.content, prompt.text, prompt.id]);

  const save = async () => {
    setSaving(true);
    setError('');
    try {
      await apiClient.updatePrompt(prompt.id ?? prompt.guid, { ...prompt, content });
      await onChanged();
    } catch (err) {
      setError(err?.message || 'Save failed');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="prompt-card">
      <div className="prompt-card-head">
        <code className="prompt-key">{prompt.key || prompt.name || prompt.id}</code>
        {prompt.description && <span className="prompt-name">{prompt.description}</span>}
      </div>
      <div className="prompt-field">
        <label htmlFor={`gp-${prompt.id}`}>{t('prompts.content')}</label>
        <textarea id={`gp-${prompt.id}`} rows={6} value={content} onChange={(e) => setContent(e.target.value)} />
      </div>
      {error && <div className="error-banner">{error}</div>}
      <div className="prompt-card-actions">
        <button type="button" className="btn btn-primary" onClick={save} disabled={saving}>
          {saving ? t('common.loading') : t('common.save')}
        </button>
      </div>
    </div>
  );
}

// One prompt key for a subject: effective content, source, global fallback, and an override editor.
function SubjectPromptCard({ prompt, subjectId, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const hasOverride = prompt.source === 'SubjectOverride' || prompt.overrideContent != null;
  const [content, setContent] = useState(prompt.overrideContent || '');
  const [mergeMode, setMergeMode] = useState(prompt.mergeMode || 'Append');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [confirmReset, setConfirmReset] = useState(false);
  const [resetting, setResetting] = useState(false);

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
    setResetting(true);
    try {
      await apiClient.deleteSubjectPrompt(subjectId, prompt.key);
      setConfirmReset(false);
      await onChanged();
    } finally {
      setResetting(false);
    }
  };

  return (
    <div className="prompt-card">
      <div className="prompt-card-head">
        <code className="prompt-key">{prompt.key}</code>
        {prompt.name && <span className="prompt-name">{prompt.name}</span>}
        <span className={`prompt-badge ${hasOverride ? 'prompt-badge-override' : 'prompt-badge-global'}`}>
          {hasOverride ? t('prompts.sourceOverride') : t('prompts.sourceGlobal')}
        </span>
        {hasOverride && (
          <span className="prompt-badge prompt-badge-mode" title={t('prompts.mergeModeTip')}>
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
        <div className="form-group prompt-mergemode">
          <label htmlFor={`mm-${prompt.key}`}>{t('prompts.mergeMode')}</label>
          <select id={`mm-${prompt.key}`} value={mergeMode} onChange={(e) => setMergeMode(e.target.value)}
            title={t('prompts.mergeModeTip')}>
            <option value="Append">{t('prompts.mergeAppend')}</option>
            <option value="Replace">{t('prompts.mergeReplace')}</option>
          </select>
        </div>
        <div className="prompt-card-actions">
          <button type="button" className="btn btn-primary" onClick={save} disabled={saving}>
            {saving ? t('common.loading') : t('common.save')}
          </button>
          <button type="button" className="btn btn-danger" onClick={() => setConfirmReset(true)} disabled={saving || !hasOverride}
            title={t('prompts.resetTip')}>
            {t('prompts.resetToGlobal')}
          </button>
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      <ConfirmModal
        isOpen={confirmReset}
        onClose={() => setConfirmReset(false)}
        onConfirm={reset}
        title={t('prompts.resetToGlobal')}
        message={t('prompts.resetConfirm', { key: prompt.key })}
        confirmLabel={t('prompts.resetToGlobal')}
        isLoading={resetting}
      />
    </div>
  );
}

export default function PromptsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [scope, setScope] = useState('global');

  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [prompts, setPrompts] = useState([]);
  const [globalPrompts, setGlobalPrompts] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    apiClient.getSubjects({ maxResults: 1000 }).then((r) => setSubjects(asArray(r, 'objects'))).catch(() => {});
  }, [apiClient]);

  const loadGlobal = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setGlobalPrompts(asArray(await apiClient.getPrompts(), 'objects', 'prompts'));
    } catch (err) {
      setError(err?.message || 'Failed to load prompts');
      setGlobalPrompts([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  const loadSubject = useCallback(async () => {
    if (!subjectId) { setPrompts([]); return; }
    setLoading(true);
    setError('');
    try {
      setPrompts(asArray(await apiClient.getSubjectPrompts(subjectId), 'objects', 'prompts'));
    } catch (err) {
      setError(err?.message || 'Failed to load prompts');
      setPrompts([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId]);

  useEffect(() => {
    if (scope === 'global') loadGlobal();
    else loadSubject();
  }, [scope, loadGlobal, loadSubject]);

  const scopeSwitch = <ScopeSwitch scope={scope} onChange={setScope} />;

  return (
    <div>
      <PageHeader
        title={t('prompts.title')}
        subtitle={scope === 'global' ? t('prompts.subtitle') : t('prompts.subjectSubtitle')}
        actions={scopeSwitch}
      />

      {scope === 'subject' && (
        <div className="prompt-filter-bar">
          <div className="pagination-group">
            <label htmlFor="prompt-subject">{t('prompts.subject')}:</label>
            <select id="prompt-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('prompts.selectSubject')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>
          <button type="button" className="btn btn-secondary" onClick={loadSubject} disabled={loading || !subjectId}>
            {t('common.refresh')}
          </button>
        </div>
      )}

      {error && <div className="error-banner">{error}</div>}

      {scope === 'global' && (
        <div className="prompt-list">
          {!loading && globalPrompts.length === 0 && !error && <div className="empty-state">{t('prompts.noneGlobal')}</div>}
          {globalPrompts.map((p) => <GlobalPromptCard key={p.id ?? p.key} prompt={p} onChanged={loadGlobal} />)}
        </div>
      )}

      {scope === 'subject' && (
        <>
          {!subjectId && <div className="empty-state">{t('prompts.pickSubject')}</div>}
          {subjectId && !loading && prompts.length === 0 && !error && <div className="empty-state">{t('prompts.noneForSubject')}</div>}
          {subjectId && (
            <div className="prompt-list">
              {prompts.map((p) => <SubjectPromptCard key={p.key} prompt={p} subjectId={subjectId} onChanged={loadSubject} />)}
            </div>
          )}
        </>
      )}
    </div>
  );
}
