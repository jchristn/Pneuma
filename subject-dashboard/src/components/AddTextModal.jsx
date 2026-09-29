import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import Modal from './Modal';
import LabelTagEditor, { toLabelTagPayload } from './LabelTagEditor';
import { useAuth } from '../context/AuthContext';

const CONTENT_TYPES = ['text/markdown', 'text/plain', 'text/html', 'application/json'];

const blankForm = (subjects) => ({
  subjectId: subjects[0]?.id || '',
  title: '',
  contentType: 'text/markdown',
  content: '',
  externalKey: '',
  labels: [],
  tags: []
});

// Push text straight into a subject (POST /v1.0/subjects/{id}/content). The content is stored and ingested like a
// link's; reusing an external key replaces earlier content instead of adding a duplicate.
function AddTextModal({ isOpen, onClose, subjects, onSubmitted }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [form, setForm] = useState(() => blankForm(subjects));
  const [preview, setPreview] = useState(false);
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setForm(blankForm(subjects));
      setPreview(false);
      setError('');
    }
  }, [isOpen, subjects]);

  const submit = async (e) => {
    e.preventDefault();
    if (!form.subjectId) {
      setError(t('addText.errorSubject'));
      return;
    }
    if (!form.content.trim()) {
      setError(t('addText.errorContent'));
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      const { labels, tags } = toLabelTagPayload(form.labels, form.tags);
      const result = await apiClient.submitContent(form.subjectId, {
        title: form.title.trim() || undefined,
        content: form.content,
        contentType: form.contentType,
        externalKey: form.externalKey.trim() || undefined,
        labels,
        tags
      });
      onSubmitted?.(result?.replaced ? t('addText.replaced') : t('addText.created'));
      onClose();
    } catch (err) {
      setError(err?.message || t('addText.errorGeneric'));
    } finally {
      setSubmitting(false);
    }
  };

  const canPreview = form.contentType === 'text/markdown';

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={t('addText.title')} size="large">
      <form onSubmit={submit}>
        {error && <div className="form-error">{error}</div>}
        <div className="form-group">
          <label htmlFor="at-subject">{t('links.subject')} <span className="required-mark">*</span></label>
          <select id="at-subject" value={form.subjectId} onChange={(e) => setForm({ ...form, subjectId: e.target.value })} required>
            <option value="" disabled>{t('links.selectSubject')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
          </select>
        </div>
        <div className="form-group" title={t('addText.titleTip')}>
          <label htmlFor="at-title">{t('addText.docTitle')}</label>
          <input id="at-title" value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} placeholder={t('addText.titlePlaceholder')} />
        </div>
        <div className="form-group" title={t('addText.contentTypeTip')}>
          <label htmlFor="at-type">{t('addText.contentType')}</label>
          <select id="at-type" value={form.contentType} onChange={(e) => setForm({ ...form, contentType: e.target.value })}>
            {CONTENT_TYPES.map((ct) => <option key={ct} value={ct}>{t(`addText.types.${ct.replace(/[/]/g, '_')}`)}</option>)}
          </select>
        </div>
        <div className="form-group">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
            <label htmlFor="at-content">{t('addText.content')} <span className="required-mark">*</span></label>
            {canPreview && (
              <button type="button" className="btn btn-secondary btn-sm" onClick={() => setPreview(!preview)} aria-pressed={preview}>
                {preview ? t('addText.edit') : t('addText.preview')}
              </button>
            )}
          </div>
          {preview && canPreview ? (
            <div className="markdown-preview" style={{ border: '1px solid var(--border-color)', borderRadius: 'var(--radius-sm)', padding: 12, minHeight: 200, maxHeight: 360, overflow: 'auto' }}>
              <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>{form.content || t('addText.emptyPreview')}</ReactMarkdown>
            </div>
          ) : (
            <textarea id="at-content" rows={12} value={form.content} onChange={(e) => setForm({ ...form, content: e.target.value })}
              placeholder={t('addText.contentPlaceholder')} style={{ fontFamily: 'var(--font-mono, monospace)' }} required />
          )}
        </div>
        <div className="form-group" title={t('addText.externalKeyTip')}>
          <label htmlFor="at-key">{t('addText.externalKey')}</label>
          <input id="at-key" value={form.externalKey} maxLength={256} onChange={(e) => setForm({ ...form, externalKey: e.target.value })} placeholder={t('addText.externalKeyPlaceholder')} />
          <p className="field-hint">{t('addText.externalKeyHint')}</p>
        </div>
        <div className="form-group">
          <label>{t('links.labelsAndTags', 'Labels & tags')}</label>
          <LabelTagEditor labels={form.labels || []} tags={form.tags || []} onChange={({ labels, tags }) => setForm({ ...form, labels, tags })} />
        </div>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={submitting}>{t('common.cancel')}</button>
          <button type="submit" className="btn btn-primary" disabled={submitting || !form.subjectId || !form.content.trim()}>
            {submitting ? t('common.loading') : t('addText.submit')}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export default AddTextModal;
