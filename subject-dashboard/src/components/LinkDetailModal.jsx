import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDateTime } from '../utils/format';
import { useAuth } from '../context/AuthContext';
import { formatFileSize, isInlineLink, linkFileName, linkKind } from '../utils/files';
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
  const { apiClient } = useAuth();
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState('');

  const inline = isInlineLink(detail);
  const fileName = linkFileName(detail);

  const download = async () => {
    setDownloading(true);
    setDownloadError('');
    try {
      await apiClient.downloadLinkSource(detail, fileName || `${detail.title || detail.id}.txt`);
    } catch (err) {
      setDownloadError(err?.message || t('upload.downloadFailed'));
    } finally {
      setDownloading(false);
    }
  };

  const close = () => {
    setDownloadError('');
    onClose();
  };

  return (
    <Modal isOpen={!!detail} onClose={close} title={t('links.linkTitle')} size="large">
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
              <span className="detail-label">{inline ? t('links.internalUri') : t('links.url')}</span>
              <span className="detail-value"><CopyableId value={detail.url} title="Copy URL" /></span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('links.kind')}</span>
              <span className="detail-value">{t(`upload.kind.${linkKind(detail)}`)}</span>
            </div>
            {fileName && (
              <div className="detail-item">
                <span className="detail-label">{t('links.fileName')}</span>
                <span className="detail-value" style={{ overflowWrap: 'anywhere' }}>{fileName}</span>
              </div>
            )}
            {detail.contentType && (
              <div className="detail-item">
                <span className="detail-label">{t('links.contentType')}</span>
                <span className="detail-value">{detail.contentType}</span>
              </div>
            )}
            {detail.sizeBytes > 0 && (
              <div className="detail-item">
                <span className="detail-label">{t('links.size')}</span>
                <span className="detail-value">{formatFileSize(detail.sizeBytes)}</span>
              </div>
            )}
            {detail.externalKey && (
              <div className="detail-item">
                <span className="detail-label">{t('links.externalKey')}</span>
                <span className="detail-value" style={{ overflowWrap: 'anywhere' }}>{detail.externalKey}</span>
              </div>
            )}
          </div>
          {inline && (
            <div style={{ marginBottom: 18, display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
              <button type="button" className="btn btn-secondary btn-sm" onClick={download} disabled={downloading}>
                {downloading ? t('common.loading') : t('links.downloadOriginal')}
              </button>
              {downloadError && <span className="form-error" style={{ margin: 0 }}>{downloadError}</span>}
            </div>
          )}
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
