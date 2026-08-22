import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import LabelTagEditor from './LabelTagEditor';

function LinkBulkSubmitModal({
  isOpen,
  onClose,
  subjects,
  bulkForm,
  setBulkForm,
  bulkError,
  bulkSubmitting,
  onSubmit
}) {
  const { t } = useTranslation();

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={t('links.addMultipleTitle')} size="medium">
      <form onSubmit={onSubmit}>
        {bulkError && <div className="form-error">{bulkError}</div>}
        <div className="form-group">
          <label htmlFor="bulk-subject">
            {t('links.subject')} <span className="required-mark">*</span>
          </label>
          <select
            id="bulk-subject"
            value={bulkForm.subjectId}
            onChange={(e) => setBulkForm({ ...bulkForm, subjectId: e.target.value })}
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
          <label htmlFor="bulk-urls">
            {t('links.urls')} <span className="required-mark">*</span>
          </label>
          <textarea
            id="bulk-urls"
            rows={9}
            value={bulkForm.urls}
            onChange={(e) => setBulkForm({ ...bulkForm, urls: e.target.value })}
            placeholder={'https://example.com/one\nhttps://example.com/two'}
            required
          />
          <p className="field-hint">{t('links.urlsHint')}</p>
        </div>
        <div className="form-group">
          <label>{t('links.labelsAndTags', 'Labels & tags')}</label>
          <LabelTagEditor
            labels={bulkForm.labels || []}
            tags={bulkForm.tags || []}
            onChange={({ labels, tags }) => setBulkForm({ ...bulkForm, labels, tags })}
          />
          <p className="field-hint">{t('links.labelsAndTagsBulkHint', 'Applied to every URL in this batch. Use them later to scope search, retrieval, and chat.')}</p>
        </div>
        <p className="field-hint">{t('links.subjectModelsHint', 'These links are ingested with the subject’s configured embedding and inference models and its collection. Configure them on the subject if it has none.')}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={bulkSubmitting}>
            {t('common.cancel')}
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={bulkSubmitting || !bulkForm.subjectId || !bulkForm.urls.trim()}
          >
            {bulkSubmitting ? t('common.loading') : t('common.submit')}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export default LinkBulkSubmitModal;
