import { useTranslation } from 'react-i18next';
import Modal from './Modal';

function LinkSubmitModal({
  isOpen,
  onClose,
  subjects,
  form,
  setForm,
  formError,
  submitting,
  onSubmit
}) {
  const { t } = useTranslation();

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={t('links.submitTitle')} size="medium">
      <form onSubmit={onSubmit}>
        {formError && <div className="form-error">{formError}</div>}
        <div className="form-group">
          <label htmlFor="ln-subject">
            {t('links.subject')} <span className="required-mark">*</span>
          </label>
          <select
            id="ln-subject"
            value={form.subjectId}
            onChange={(e) => setForm({ ...form, subjectId: e.target.value })}
            required
          >
            <option value="" disabled>
              {t('links.selectSubject')}
            </option>
            {subjects.map((c) => (
              <option key={c.id} value={c.id}>
                {c.displayName || c.id}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="ln-url">
            {t('links.url')} <span className="required-mark">*</span>
          </label>
          <input
            id="ln-url"
            type="url"
            value={form.url}
            onChange={(e) => setForm({ ...form, url: e.target.value })}
            placeholder="https://example.com/my-content"
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="ln-title">{t('links.linkTitle')}</label>
          <input
            id="ln-title"
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            placeholder="Optional title"
          />
        </div>
        <p className="field-hint">{t('links.subjectModelsHint', 'This link is ingested with the subject’s configured embedding and inference models and its collection. Configure them on the subject if it has none.')}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={submitting}>
            {t('common.cancel')}
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting || !form.subjectId || !form.url}
          >
            {submitting ? t('common.loading') : t('common.submit')}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export default LinkSubmitModal;
