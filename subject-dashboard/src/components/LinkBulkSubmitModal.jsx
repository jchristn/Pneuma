import { useTranslation } from 'react-i18next';
import Modal from './Modal';

function endpointLabel(ep) {
  return ep.name || ep.model || ep.id;
}

function collectionLabel(c) {
  const name = c.name || c.Name || c.id || c.Id;
  const dims = c.dimensionality ?? c.Dimensionality;
  return dims ? `${name} (${dims}d)` : name;
}

function LinkBulkSubmitModal({
  isOpen,
  onClose,
  subjects,
  embeddingEndpoints,
  completionEndpoints,
  collections = [],
  hasEndpoints,
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
            rows={6}
            value={bulkForm.urls}
            onChange={(e) => setBulkForm({ ...bulkForm, urls: e.target.value })}
            placeholder={'https://example.com/one\nhttps://example.com/two'}
            required
          />
          <p className="field-hint">{t('links.urlsHint')}</p>
        </div>
        <div className="form-group">
          <label htmlFor="bulk-embedding">
            {t('links.embeddingModel')} <span className="required-mark">*</span>
          </label>
          <select
            id="bulk-embedding"
            value={bulkForm.embeddingEndpointId}
            onChange={(e) => setBulkForm({ ...bulkForm, embeddingEndpointId: e.target.value })}
            required
            disabled={!hasEndpoints}
          >
            <option value="" disabled>
              {t('links.selectModel')}
            </option>
            {embeddingEndpoints.map((ep) => (
              <option key={ep.id} value={ep.id}>
                {endpointLabel(ep)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="bulk-completion">
            {t('links.completionModel')} <span className="required-mark">*</span>
          </label>
          <select
            id="bulk-completion"
            value={bulkForm.completionEndpointId}
            onChange={(e) => setBulkForm({ ...bulkForm, completionEndpointId: e.target.value })}
            required
            disabled={!hasEndpoints}
          >
            <option value="" disabled>
              {t('links.selectModel')}
            </option>
            {completionEndpoints.map((ep) => (
              <option key={ep.id} value={ep.id}>
                {endpointLabel(ep)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="bulk-collection">
            {t('links.collection')} <span className="required-mark">*</span>
          </label>
          <select
            id="bulk-collection"
            value={bulkForm.collectionId}
            onChange={(e) => setBulkForm({ ...bulkForm, collectionId: e.target.value })}
            required
            disabled={collections.length === 0}
          >
            <option value="" disabled>
              {t('links.selectCollection')}
            </option>
            {collections.map((c) => (
              <option key={c.id ?? c.Id} value={c.id ?? c.Id}>
                {collectionLabel(c)}
              </option>
            ))}
          </select>
        </div>
        {!hasEndpoints && <div className="form-error">{collections.length === 0 ? t('links.noCollections') : t('links.noEndpoints')}</div>}
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={bulkSubmitting}>
            {t('common.cancel')}
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={bulkSubmitting || !hasEndpoints || !bulkForm.embeddingEndpointId || !bulkForm.completionEndpointId || !bulkForm.collectionId}
          >
            {bulkSubmitting ? t('common.loading') : t('common.submit')}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export default LinkBulkSubmitModal;
