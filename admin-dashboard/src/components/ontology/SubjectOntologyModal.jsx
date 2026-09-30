import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { normalizeList } from '../../utils/api';
import Modal from '../Modal';
import StatusPill from '../StatusPill';
import ConfirmModal from '../ConfirmModal';
import { ViolationsPanel, OperationsPanel } from './SubjectOntologyPanels';
import { GRAPH_FORMATS, FORMAT_FILES, downloadText, exportText, ontologyError } from './ontologyUtils';
import './Ontology.css';

const TABS = ['overview', 'violations', 'operations', 'export'];

// Every approved version in the tenant, as { value, label } options.
async function approvedVersionOptions(apiClient) {
  const ontologies = normalizeList(await apiClient.listOntologies()).items;
  const details = await Promise.all(ontologies.map((o) => apiClient.getOntology(o.id).catch(() => null)));
  const options = [];
  details.filter(Boolean).forEach((d) => {
    (d.versions || []).filter((v) => v.status === 'Approved').forEach((v) => options.push({ value: v.id, label: `${d.ontology.name} · v${v.versionNumber}` }));
  });
  return options;
}

// How one subject classifies: its pinned ontology version (or the prompt-only definition), classification
// settings and cache, violations, background operations, and graph export.
function SubjectOntologyModal({ subject, onClose, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [view, setView] = useState(null);
  const [options, setOptions] = useState([]);
  const [selected, setSelected] = useState('');
  const [retag, setRetag] = useState(true);
  const [tab, setTab] = useState('overview');
  const [format, setFormat] = useState('json');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [confirmClear, setConfirmClear] = useState(false);

  const load = useCallback(async () => {
    try {
      const v = await apiClient.getSubjectOntology(subject.id);
      setView(v);
      setSelected(v.version?.id || '');
      setError('');
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
    }
  }, [apiClient, subject.id, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { approvedVersionOptions(apiClient).then(setOptions).catch(() => setOptions([])); }, [apiClient]);

  const run = async (fn, doneMessage) => {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await fn();
      if (doneMessage) setNotice(doneMessage);
      await load();
      onChanged?.();
    } catch (err) {
      setError(ontologyError(err, t('ontology.actionError')));
    } finally {
      setBusy(false);
    }
  };

  const pin = () => run(() => apiClient.setSubjectOntology(subject.id, selected || null, retag),
    selected ? t('ontology.pinnedNotice') : t('ontology.unpinnedNotice'));

  const download = () => run(async () => {
    const text = await apiClient.exportSubjectGraph(subject.id, format);
    const file = FORMAT_FILES[format];
    const safe = (subject.urlSlug || subject.displayName || 'subject').replace(/[^a-z0-9]+/gi, '-').toLowerCase();
    downloadText(`${safe}-graph.${file.ext}`, exportText(format, text), file.mime);
  });

  const pinnedId = view?.version?.id || '';
  return (
    <Modal title={t('ontology.subjectTitle', { name: subject.displayName || subject.id })} size="xl" onClose={busy ? () => {} : onClose}
      footer={<button type="button" className="button-primary" onClick={onClose} disabled={busy}>{t('common.close')}</button>}>
      <div className="segmented" role="tablist" aria-label={t('ontology.title')}>
        {TABS.map((key) => (
          <button key={key} type="button" role="tab" aria-selected={tab === key} className={`segmented-btn ${tab === key ? 'active' : ''}`} onClick={() => setTab(key)}>
            {t(`ontology.subjectTab.${key}`)}
          </button>
        ))}
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
      {notice && <div className="field-hint" role="status">{notice}</div>}
      <div className="onto-section">
        {tab === 'overview' && view && (
          <>
            <dl className="kv-grid">
              <dt>{t('ontology.definitionSource')}</dt>
              <dd>
                <StatusPill label={view.source === 'Version' ? t('ontology.sourceOntology') : t('ontology.sourcePrompt')} tone={view.source === 'Version' ? 'success' : 'neutral'} />
                {view.ontology && ` ${view.ontology.name} · v${view.version?.versionNumber}`}
              </dd>
              <dt title={t('ontology.temperatureTip')}>{t('ontology.temperature')}</dt><dd>{view.classificationTemperature}</dd>
              <dt title={t('ontology.cacheTip')}>{t('ontology.cache')}</dt>
              <dd>{view.classificationCacheEnabled ? t('ontology.cacheOn', { count: view.cacheEntries }) : t('ontology.cacheOff')}</dd>
              <dt>{t('ontology.quarantined')}</dt><dd>{view.quarantinedCount}</dd>
              <dt>{t('ontology.concepts')}</dt><dd>{view.conceptCount}</dd>
            </dl>
            <p className="field-hint">{t('ontology.subjectSettingsHint')}</p>
            <div className="form-grid form-grid-2col">
              <div className="field">
                <label htmlFor="onto-pin" className="has-tip" title={t('ontology.pinTip')}>{t('ontology.pinnedVersion')}</label>
                <select id="onto-pin" value={selected} onChange={(e) => setSelected(e.target.value)} disabled={busy}>
                  <option value="">{t('ontology.noPin')}</option>
                  {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
              <label className="checkbox-field" title={t('ontology.retagTip')}>
                <input type="checkbox" checked={retag} onChange={(e) => setRetag(e.target.checked)} disabled={busy} />
                <span>{t('ontology.retagOnPin')}</span>
              </label>
            </div>
            <div className="onto-toolbar">
              <button type="button" className="button-primary" onClick={pin} disabled={busy || selected === pinnedId}>{selected ? t('ontology.pin') : t('ontology.unpin')}</button>
              <button type="button" className="button-secondary" onClick={() => setConfirmClear(true)} disabled={busy} title={t('ontology.clearCacheTip')}>{t('ontology.clearCache')}</button>
            </div>
            <div className="field">
              <label title={t('ontology.effectiveDefinitionTip')}>{t('ontology.effectiveDefinition')}</label>
              <div className="onto-pre">{view.effectiveDefinition || '—'}</div>
            </div>
          </>
        )}
        {tab === 'violations' && <ViolationsPanel subjectId={subject.id} onChanged={load} />}
        {tab === 'operations' && <OperationsPanel subjectId={subject.id} />}
        {tab === 'export' && (
          <div>
            <p className="field-hint">{t('ontology.graphExportHint')}</p>
            <div className="onto-toolbar">
              <select value={format} onChange={(e) => setFormat(e.target.value)} aria-label={t('ontology.format')}>
                {GRAPH_FORMATS.map((f) => <option key={f} value={f}>{t(`ontology.graphFormat.${f}`)}</option>)}
              </select>
              <button type="button" className="button-primary" onClick={download} disabled={busy}>{busy ? t('common.loading') : t('ontology.download')}</button>
            </div>
          </div>
        )}
      </div>
      {confirmClear && (
        <ConfirmModal title={t('ontology.clearCache')} message={t('ontology.clearCacheConfirm')} confirmLabel={t('ontology.clearCache')}
          onConfirm={async () => {
            const r = await apiClient.clearClassificationCache(subject.id);
            setNotice(t('ontology.cacheCleared', { count: r?.removed ?? 0 }));
            await load();
          }}
          onClose={() => setConfirmClear(false)} />
      )}
    </Modal>
  );
}

export default SubjectOntologyModal;
