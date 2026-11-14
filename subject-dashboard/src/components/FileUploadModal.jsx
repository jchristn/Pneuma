import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import LabelTagEditor, { toLabelTagPayload } from './LabelTagEditor';
import { useAuth } from '../context/AuthContext';
import { useUploadQueue } from '../hooks/useUploadQueue';
import { acceptAttribute, dragHasFiles, filesFromDrop, formatFileSize } from '../utils/files';
import './FileUpload.css';

// Upload files into a subject (POST /v1.0/subjects/{id}/files), one request per file with at most three in flight.
// Files come from the drop zone, the browse button, or a drop anywhere on the Links page (`incomingFiles`).
function FileUploadModal({ isOpen, onClose, subjects, defaultSubjectId = '', incomingFiles = null, onIncomingConsumed, onUploaded, onViewLog }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [settings, setSettings] = useState(null);
  const [subjectId, setSubjectId] = useState('');
  const [replaceSame, setReplaceSame] = useState(false);
  const [labels, setLabels] = useState([]);
  const [tags, setTags] = useState([]);
  const [over, setOver] = useState(false);
  const [error, setError] = useState('');
  const [confirmClose, setConfirmClose] = useState(false);
  const inputRef = useRef(null);
  const queue = useUploadQueue(settings);
  const { add, reset, start, pending, counts } = queue;
  const reportedRef = useRef(0);

  useEffect(() => {
    if (!isOpen || !apiClient) return;
    let cancelled = false;
    apiClient.getUploadSettings()
      .then((s) => { if (!cancelled) setSettings(s); })
      .catch(() => { if (!cancelled) setSettings(null); });
    return () => { cancelled = true; };
  }, [isOpen, apiClient]);

  useEffect(() => {
    if (!isOpen) return;
    setSubjectId(defaultSubjectId || subjects[0]?.id || '');
    setReplaceSame(false);
    setLabels([]);
    setTags([]);
    setError('');
    reportedRef.current = 0;
    reset();
    // Only when the modal opens; subject list changes must not wipe the queue.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen || !incomingFiles || incomingFiles.length === 0) return;
    add(incomingFiles);
    onIncomingConsumed?.();
  }, [isOpen, incomingFiles, add, onIncomingConsumed]);

  // Refresh the links list once a run finishes with something uploaded.
  useEffect(() => {
    const done = counts.done || 0;
    if (pending === 0 && done > reportedRef.current) {
      reportedRef.current = done;
      onUploaded?.(done);
    }
  }, [pending, counts.done, onUploaded]);

  const send = useCallback((item, onProgress, signal) => {
    const { labels: l, tags: tg } = toLabelTagPayload(labels, tags);
    return apiClient.uploadFile(subjectId, item.file, {
      externalKey: replaceSame ? item.file.name.slice(0, 256) : undefined,
      labels: l,
      tags: tg
    }, onProgress, signal);
  }, [apiClient, subjectId, replaceSame, labels, tags]);

  const begin = () => {
    if (!subjectId) {
      setError(t('upload.noSubject'));
      return;
    }
    setError('');
    start(send);
  };

  const retry = (id) => {
    queue.retry(id);
    start(send);
  };

  const pick = () => {
    if (inputRef.current) inputRef.current.click();
  };

  const onInput = (e) => {
    add(e.target.files);
    e.target.value = '';
  };

  const onZoneDragOver = (e) => {
    if (!dragHasFiles(e)) return;
    e.preventDefault();
    e.stopPropagation();
    setOver(true);
  };

  const onZoneDrop = async (e) => {
    if (!dragHasFiles(e)) return;
    e.preventDefault();
    e.stopPropagation();
    setOver(false);
    add(await filesFromDrop(e.dataTransfer));
  };

  const requestClose = () => {
    if (pending > 0) setConfirmClose(true);
    else onClose();
  };

  const accept = useMemo(() => acceptAttribute(settings), [settings]);
  const queuedCount = counts.queued || 0;
  const hint = settings?.maxUploadBytes
    ? t('upload.hint', { max: formatFileSize(settings.maxUploadBytes) })
    : t('upload.hintNoLimit');

  const statusText = (item) => {
    switch (item.status) {
      case 'uploading': {
        const pct = item.file.size ? Math.min(100, Math.round((item.loaded / item.file.size) * 100)) : 0;
        return t('upload.status.uploading', { percent: pct });
      }
      case 'done': return item.result?.replaced ? t('upload.status.replaced') : t('upload.status.done');
      default: return t(`upload.status.${item.status}`);
    }
  };

  const errorText = (item) => {
    if (item.status === 'rejected' && item.reject) return t(`upload.reject.${item.reject.code}`, item.reject.params);
    if (item.status !== 'failed') return '';
    if (item.errorStatus === 415) return `${t('upload.error415')} ${item.error}`;
    if (item.errorStatus === 413) return `${t('upload.error413')} ${item.error}`;
    return item.error;
  };

  return (
    <>
      <Modal isOpen={isOpen} onClose={requestClose} title={t('upload.title')} size="large">
        <div className="fu-root">
          {error && <div className="form-error">{error}</div>}
          <div className="form-group">
            <label htmlFor="fu-subject">{t('links.subject')} <span className="required-mark">*</span></label>
            <select id="fu-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} disabled={queue.busy}>
              <option value="" disabled>{t('links.selectSubject')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>

          <div
            className={`fu-dropzone${over ? ' is-over' : ''}`}
            role="button"
            tabIndex={0}
            aria-label={t('upload.browse')}
            onClick={pick}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } }}
            onDragEnter={onZoneDragOver}
            onDragOver={onZoneDragOver}
            onDragLeave={() => setOver(false)}
            onDrop={onZoneDrop}
          >
            <span className="fu-dropzone-icon" aria-hidden="true">⇪</span>
            <span className="fu-dropzone-title">{t('upload.dropTitle')}</span>
            <span>{t('upload.or')} <span className="fu-link-button" style={{ padding: 0 }}>{t('upload.browse')}</span></span>
            <span className="fu-dropzone-hint">{hint}</span>
            <input ref={inputRef} type="file" multiple hidden accept={accept || undefined} onChange={onInput} />
          </div>

          {queue.items.length === 0 ? (
            <p className="fu-queue-empty">{t('upload.empty')}</p>
          ) : (
            <ul className="fu-queue" aria-live="polite">
              {queue.items.map((item) => (
                <li key={item.id} className="fu-item">
                  <span className="fu-item-name" title={item.file.name}>{item.file.name}</span>
                  <span className="fu-item-meta">
                    <span>{formatFileSize(item.file.size)}</span>
                    <span className={`fu-item-status is-${item.status}`}>{statusText(item)}</span>
                  </span>
                  {item.status === 'uploading' && (
                    <span className="fu-progress" aria-hidden="true">
                      <span style={{ width: `${item.file.size ? Math.min(100, (item.loaded / item.file.size) * 100) : 0}%` }} />
                    </span>
                  )}
                  {errorText(item) && <span className="fu-item-error">{errorText(item)}</span>}
                  <span className="fu-item-actions">
                    {item.status === 'done' && item.result?.link && onViewLog && (
                      <button type="button" className="fu-link-button" onClick={() => onViewLog(item.result.link)}>{t('upload.viewLog')}</button>
                    )}
                    {item.status === 'failed' && (
                      <button type="button" className="fu-link-button" onClick={() => retry(item.id)}>{t('upload.retry')}</button>
                    )}
                    {item.status !== 'uploading' && (
                      <button type="button" className="fu-link-button is-danger" onClick={() => queue.remove(item.id)} aria-label={`${t('upload.remove')} ${item.file.name}`}>
                        {t('upload.remove')}
                      </button>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {queue.items.length > 0 && (
            <div className="fu-summary">
              {t('upload.summary', { done: counts.done || 0, failed: (counts.failed || 0) + (counts.rejected || 0), pending })}
              {' '}
              {(counts.done || counts.failed || counts.rejected || counts.cancelled) ? (
                <button type="button" className="fu-link-button" onClick={queue.clearFinished} disabled={queue.busy}>{t('upload.clearFinished')}</button>
              ) : null}
            </div>
          )}

          <div className="form-group" style={{ marginTop: 16 }}>
            <label className="fu-check" htmlFor="fu-replace">
              <input id="fu-replace" type="checkbox" checked={replaceSame} onChange={(e) => setReplaceSame(e.target.checked)} disabled={queue.busy} />
              <span>
                {t('upload.replaceSame')}
                <span className="field-hint" style={{ display: 'block' }}>{t('upload.replaceSameHint')}</span>
              </span>
            </label>
          </div>
          <div className="form-group">
            <label>{t('links.labelsAndTags', 'Labels & tags')}</label>
            <LabelTagEditor labels={labels} tags={tags} onChange={({ labels: l, tags: tg }) => { setLabels(l); setTags(tg); }} />
            <p className="field-hint">{t('upload.labelsHint')}</p>
          </div>

          <div className="form-actions">
            {pending > 0 ? (
              <button type="button" className="btn btn-secondary" onClick={queue.cancelAll}>{t('upload.cancelAll')}</button>
            ) : (
              <button type="button" className="btn btn-secondary" onClick={onClose}>{t('common.close')}</button>
            )}
            <button type="button" className="btn btn-primary" onClick={begin} disabled={queue.busy || queuedCount === 0 || !subjectId}>
              {queue.busy ? t('upload.uploading') : t('upload.start', { count: queuedCount })}
            </button>
          </div>
        </div>
      </Modal>
      <ConfirmModal
        isOpen={confirmClose}
        onClose={() => setConfirmClose(false)}
        onConfirm={() => { queue.cancelAll(); setConfirmClose(false); onClose(); }}
        title={t('upload.confirmCloseTitle')}
        message={t('upload.confirmCloseMessage', { count: pending })}
        confirmLabel={t('upload.cancelAll')}
        variant="warning"
      />
    </>
  );
}

export default FileUploadModal;
