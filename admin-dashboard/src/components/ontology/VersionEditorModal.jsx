import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import StatusPill from '../StatusPill';
import TypeListEditor from './TypeListEditor';
import RuleListEditor from './RuleListEditor';
import ConceptListEditor from './ConceptListEditor';
import { UNDECLARED_ACTIONS, versionTone, ontologyError } from './ontologyUtils';
import './Ontology.css';

const TABS = ['general', 'nodeTypes', 'edgeTypes', 'rules', 'concepts'];

// View or edit one ontology version. Drafts are editable; approved and retired versions are read-only.
function VersionEditorModal({ versionId, onClose, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [version, setVersion] = useState(null);
  const [tab, setTab] = useState('general');
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setError('');
    try {
      setVersion(await apiClient.getOntologyVersion(versionId));
      setDirty(false);
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
    }
  }, [apiClient, versionId, t]);

  useEffect(() => { load(); }, [load]);

  const readOnly = !version || version.status !== 'Draft';
  const patch = (p) => { setVersion((v) => ({ ...v, ...p })); setDirty(true); };

  const save = async () => {
    setBusy(true);
    setError('');
    try {
      const saved = await apiClient.updateOntologyVersion(versionId, version);
      setVersion(saved);
      setDirty(false);
      onChanged?.();
    } catch (err) {
      setError(ontologyError(err, t('ontology.saveError')));
    } finally {
      setBusy(false);
    }
  };

  const footer = (
    <>
      <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.close')}</button>
      {!readOnly && (
        <button type="button" className="button-primary" onClick={save} disabled={busy || !dirty}>{busy ? t('common.loading') : t('common.save')}</button>
      )}
    </>
  );

  const title = version ? t('ontology.versionTitle', { number: version.versionNumber }) : t('ontology.version');
  return (
    <Modal title={title} size="xl" onClose={busy ? () => {} : onClose} footer={footer}
      headerExtra={version && <StatusPill label={t(`ontology.status.${version.status}`)} tone={versionTone(version.status)} />}>
      {error && <div className="error-message onto-error">{error}</div>}
      {!version && !error && <div className="table-loading">{t('common.loading')}</div>}
      {version && (
        <>
          {readOnly && <p className="field-hint">{t('ontology.readOnlyHint')}</p>}
          {version.problems?.length > 0 && (
            <div className="onto-problems" role="status">
              <strong>{t('ontology.problemsTitle')}</strong>
              <ul>{version.problems.map((p) => <li key={p}>{p}</li>)}</ul>
            </div>
          )}
          <div className="segmented" role="tablist" aria-label={t('ontology.version')}>
            {TABS.map((key) => (
              <button key={key} type="button" role="tab" aria-selected={tab === key} className={`segmented-btn ${tab === key ? 'active' : ''}`} onClick={() => setTab(key)}>
                {t(`ontology.tab.${key}`)}
              </button>
            ))}
          </div>
          <div className="onto-section">
            {tab === 'general' && (
              <div className="form-grid form-grid-2col">
                <div className="field">
                  <label htmlFor="onto-undeclared" className="has-tip" title={t('ontology.undeclaredTip')}>{t('ontology.undeclared')}</label>
                  <select id="onto-undeclared" value={version.undeclaredTypeAction} disabled={readOnly} onChange={(e) => patch({ undeclaredTypeAction: e.target.value })}>
                    {UNDECLARED_ACTIONS.map((a) => <option key={a} value={a}>{t(`ontology.undeclaredAction.${a}`)}</option>)}
                  </select>
                  <small className="field-hint">{t(`ontology.undeclaredActionTip.${version.undeclaredTypeAction}`)}</small>
                </div>
                <div className="field">
                  <label htmlFor="onto-summary" className="has-tip" title={t('ontology.changeSummaryTip')}>{t('ontology.changeSummary')}</label>
                  <input id="onto-summary" value={version.changeSummary || ''} disabled={readOnly} onChange={(e) => patch({ changeSummary: e.target.value || null })} />
                </div>
                <div className="field field-full">
                  <label htmlFor="onto-guidance" className="has-tip" title={t('ontology.guidanceTip')}>{t('ontology.guidance')}</label>
                  <textarea id="onto-guidance" rows={6} value={version.guidance || ''} disabled={readOnly} onChange={(e) => patch({ guidance: e.target.value || null })} />
                  <small className="field-hint">{t('ontology.guidanceHint')}</small>
                </div>
              </div>
            )}
            {tab === 'nodeTypes' && (
              <TypeListEditor items={version.nodeTypes || []} readOnly={readOnly} placeholder="Person" onChange={(nodeTypes) => patch({ nodeTypes })} />
            )}
            {tab === 'edgeTypes' && (
              <TypeListEditor items={version.edgeTypes || []} readOnly={readOnly} placeholder="WORKS_FOR" onChange={(edgeTypes) => patch({ edgeTypes })} />
            )}
            {tab === 'rules' && (
              <RuleListEditor rules={version.rules || []} readOnly={readOnly} nodeTypes={version.nodeTypes || []} edgeTypes={version.edgeTypes || []}
                onChange={(rules) => patch({ rules })} />
            )}
            {tab === 'concepts' && (
              <>
                <datalist id="onto-node-types">{(version.nodeTypes || []).map((n) => <option key={n.name} value={n.name} />)}</datalist>
                <ConceptListEditor concepts={version.concepts || []} readOnly={readOnly} onChange={(concepts) => patch({ concepts })} />
              </>
            )}
          </div>
          {dirty && <p className="field-hint">{t('ontology.unsaved')}</p>}
        </>
      )}
    </Modal>
  );
}

export default VersionEditorModal;
