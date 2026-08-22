import { useTranslation } from 'react-i18next';
import { formatDateTime } from '../utils/format';
import Modal from './Modal';
import StatusPill from './StatusPill';
import CopyableId from './CopyableId';
import JsonViewer from './JsonViewer';

function isFailed(status) {
  const s = (status || '').toLowerCase();
  return s === 'failed' || s === 'error';
}

function LinkDetailModal({ detail, subjectName, onClose }) {
  const { t } = useTranslation();

  return (
    <Modal isOpen={!!detail} onClose={onClose} title={t('links.linkTitle')} size="large">
      {detail && (
        <div>
          <div className="detail-grid" style={{ marginBottom: 18 }}>
            <div className="detail-item">
              <span className="detail-label">{t('links.linkTitle')}</span>
              <span className="detail-value">{detail.title || '(untitled)'}</span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('common.status')}</span>
              <span className="detail-value"><StatusPill status={detail.status} /></span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('links.subject')}</span>
              <span className="detail-value">{subjectName[detail.subjectId] || '—'}</span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('links.lastIngested')}</span>
              <span className="detail-value">
                {detail.lastIngestedUtc ? formatDateTime(detail.lastIngestedUtc) : t('common.never')}
              </span>
            </div>
            <div className="detail-item">
              <span className="detail-label">ID</span>
              <span className="detail-value"><CopyableId value={detail.id} /></span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('links.url')}</span>
              <span className="detail-value"><CopyableId value={detail.url} title="Copy URL" /></span>
            </div>
          </div>
          {((Array.isArray(detail.labels) && detail.labels.length > 0) || (detail.tags && Object.keys(detail.tags).length > 0)) && (
            <div className="detail-grid" style={{ marginBottom: 18 }}>
              {Array.isArray(detail.labels) && detail.labels.length > 0 && (
                <div className="detail-item">
                  <span className="detail-label">{t('links.labels', 'Labels')}</span>
                  <span className="detail-value lt-chips">
                    {detail.labels.map((l) => (<span key={l} className="lt-chip">{l}</span>))}
                  </span>
                </div>
              )}
              {detail.tags && Object.keys(detail.tags).length > 0 && (
                <div className="detail-item">
                  <span className="detail-label">{t('links.tags', 'Tags')}</span>
                  <span className="detail-value lt-chips">
                    {Object.entries(detail.tags).map(([k, v]) => (<span key={k} className="lt-chip">{k}: {v}</span>))}
                  </span>
                </div>
              )}
            </div>
          )}
          {isFailed(detail.status) && detail.lastError && (
            <div className="form-error" style={{ marginBottom: 18 }}>{detail.lastError}</div>
          )}
          <JsonViewer value={detail} label="Raw JSON" />
        </div>
      )}
    </Modal>
  );
}

export default LinkDetailModal;
