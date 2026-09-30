import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { asArray } from '../../utils/api';
import { OntoModal as Modal } from './OntoKit';
import { ontologyError, downloadText, exportText, FORMAT_FILES } from './ontologyUtils';
import './Ontology.css';

// Shared footer: cancel + a primary action that shows a busy state.
function ActionFooter({ busy, onClose, onGo, label, disabled = false }) {
  const { t } = useTranslation();
  return (
    <>
      <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
      <button type="button" className="btn btn-primary" onClick={onGo} disabled={busy || disabled}>{busy ? t('common.loading') : label}</button>
    </>
  );
}

// Run an async action with busy/error state; closes on success.
function useAction(onClose) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const run = async (fn) => {
    setBusy(true);
    setError('');
    try {
      await fn();
      onClose();
    } catch (err) {
      setError(ontologyError(err, t('ontology.actionError')));
      setBusy(false);
    }
  };
  return { busy, error, run };
}

// Approve a draft, recording a change summary. Approval fails (with the listed problems) while the draft has any.
export function ApproveModal({ version, onClose, onDone }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [summary, setSummary] = useState(version.changeSummary || '');
  const { busy, error, run } = useAction(onClose);
  return (
    <Modal title={t('ontology.approveTitle', { number: version.versionNumber })} size="sm" onClose={busy ? () => {} : onClose}
      footer={<ActionFooter busy={busy} onClose={onClose} label={t('ontology.approve')}
        onGo={() => run(async () => { await apiClient.approveOntologyVersion(version.id, summary || null); onDone?.(); })} />}>
      <p className="confirm-text">{t('ontology.approveMessage')}</p>
      <div className="form-group">
        <label htmlFor="onto-approve-summary">{t('ontology.changeSummary')}</label>
        <textarea id="onto-approve-summary" rows={3} value={summary} onChange={(e) => setSummary(e.target.value)} />
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
    </Modal>
  );
}

// Have the inference model propose a new draft from a subject's content or pasted sample text.
export function ProposeModal({ ontology, versions, onClose, onDone }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [runners, setRunners] = useState([]);
  const [form, setForm] = useState({ subjectId: '', sampleText: '', modelRunnerId: '', sampleCells: 20, instructions: '', language: '', basedOnVersionId: versions[0]?.id || '' });
  const { busy, error, run } = useAction(onClose);
  useEffect(() => {
    apiClient.getSubjects({ query: { maxResults: 1000 } }).then((r) => setSubjects(asArray(r))).catch(() => {});
    apiClient.get('/v1.0/model-runners', { query: { maxResults: 1000 } }).then((r) => setRunners(asArray(r))).catch(() => {});
  }, [apiClient]);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));
  const go = () => run(async () => {
    const body = { ...form, sampleCells: Number(form.sampleCells) || 20 };
    Object.keys(body).forEach((k) => { if (body[k] === '') body[k] = null; });
    const draft = await apiClient.proposeOntology(ontology.id, body);
    onDone?.(draft);
  });
  return (
    <Modal title={t('ontology.proposeTitle', { name: ontology.name })} size="lg" onClose={busy ? () => {} : onClose}
      footer={<ActionFooter busy={busy} onClose={onClose} onGo={go} label={t('ontology.propose')} disabled={!form.subjectId && !form.sampleText.trim()} />}>
      <p className="field-hint">{t('ontology.proposeHint')}</p>
      <div className="form-grid form-grid-2col">
        <div className="form-group">
          <label htmlFor="onto-p-subject" className="has-tip" title={t('ontology.proposeSubjectTip')}>{t('ontology.subject')}</label>
          <select id="onto-p-subject" value={form.subjectId} onChange={(e) => set('subjectId', e.target.value)}>
            <option value="">{t('common.none')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="onto-p-runner" className="has-tip" title={t('ontology.proposeRunnerTip')}>{t('ontology.modelRunner')}</label>
          <select id="onto-p-runner" value={form.modelRunnerId} onChange={(e) => set('modelRunnerId', e.target.value)}>
            <option value="">{t('ontology.subjectModel')}</option>
            {runners.map((r) => <option key={r.id} value={r.id}>{r.name || r.model || r.id}</option>)}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="onto-p-cells" className="has-tip" title={t('ontology.sampleCellsTip')}>{t('ontology.sampleCells')}</label>
          <input id="onto-p-cells" type="number" min={1} max={200} value={form.sampleCells} onChange={(e) => set('sampleCells', e.target.value)} />
        </div>
        <div className="form-group">
          <label htmlFor="onto-p-base" className="has-tip" title={t('ontology.basedOnTip')}>{t('ontology.basedOn')}</label>
          <select id="onto-p-base" value={form.basedOnVersionId} onChange={(e) => set('basedOnVersionId', e.target.value)}>
            <option value="">{t('common.none')}</option>
            {versions.map((v) => <option key={v.id} value={v.id}>{t('ontology.versionTitle', { number: v.versionNumber })} · {t(`ontology.status.${v.status}`)}</option>)}
          </select>
        </div>
        <div className="form-group field-full">
          <label htmlFor="onto-p-text" className="has-tip" title={t('ontology.sampleTextTip')}>{t('ontology.sampleText')}</label>
          <textarea id="onto-p-text" rows={5} value={form.sampleText} onChange={(e) => set('sampleText', e.target.value)} />
        </div>
        <div className="form-group field-full">
          <label htmlFor="onto-p-instr" className="has-tip" title={t('ontology.instructionsTip')}>{t('ontology.instructions')}</label>
          <textarea id="onto-p-instr" rows={3} value={form.instructions} onChange={(e) => set('instructions', e.target.value)} />
        </div>
        <div className="form-group">
          <label htmlFor="onto-p-lang">{t('ontology.language')}</label>
          <input id="onto-p-lang" value={form.language} placeholder="en" onChange={(e) => set('language', e.target.value)} />
        </div>
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
    </Modal>
  );
}

// Import a SKOS taxonomy (Turtle or JSON-LD) into a draft, merging with or replacing its concepts.
export function TaxonomyImportModal({ version, onClose, onDone }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [format, setFormat] = useState('turtle');
  const [mode, setMode] = useState('merge');
  const [text, setText] = useState('');
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const readFile = (file) => {
    if (!file) return;
    if (/\.(jsonld|json)$/i.test(file.name)) setFormat('jsonld');
    file.text().then(setText).catch(() => setError(t('ontology.fileError')));
  };
  const go = async () => {
    setBusy(true);
    setError('');
    try {
      setResult(await apiClient.importTaxonomy(version.id, text, format, mode));
      onDone?.();
    } catch (err) {
      setError(ontologyError(err, t('ontology.importError')));
    } finally {
      setBusy(false);
    }
  };
  const footer = result
    ? <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
    : <ActionFooter busy={busy} onClose={onClose} onGo={go} label={t('ontology.import')} disabled={!text.trim()} />;
  return (
    <Modal title={t('ontology.importTitle', { number: version.versionNumber })} size="lg" onClose={busy ? () => {} : onClose} footer={footer}>
      {result ? (
        <div>
          <p className="confirm-text">{t('ontology.importResult', result)}</p>
          {result.warnings?.length > 0 && (
            <div className="onto-problems"><strong>{t('ontology.warnings')}</strong><ul>{result.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>
          )}
        </div>
      ) : (
        <div className="form-grid form-grid-2col">
          <p className="field-hint field-full">{t('ontology.importHint')}</p>
          <div className="form-group">
            <label htmlFor="onto-i-format">{t('ontology.format')}</label>
            <select id="onto-i-format" value={format} onChange={(e) => setFormat(e.target.value)}>
              <option value="turtle">Turtle</option>
              <option value="jsonld">JSON-LD</option>
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="onto-i-mode" className="has-tip" title={t('ontology.importModeTip')}>{t('ontology.importMode')}</label>
            <select id="onto-i-mode" value={mode} onChange={(e) => setMode(e.target.value)}>
              <option value="merge">{t('ontology.modeMerge')}</option>
              <option value="replace">{t('ontology.modeReplace')}</option>
            </select>
          </div>
          <div className="form-group field-full">
            <label htmlFor="onto-i-file">{t('ontology.file')}</label>
            <input id="onto-i-file" type="file" accept=".ttl,.jsonld,.json,text/turtle,application/ld+json" onChange={(e) => readFile(e.target.files?.[0])} />
          </div>
          <div className="form-group field-full">
            <label htmlFor="onto-i-text">{t('ontology.document')}</label>
            <textarea id="onto-i-text" rows={10} value={text} onChange={(e) => setText(e.target.value)} spellCheck={false} />
          </div>
        </div>
      )}
      {error && <div className="error-message onto-error">{error}</div>}
    </Modal>
  );
}

// What changed between a version and another (by default the version it was copied from).
export function DiffModal({ version, versions, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [against, setAgainst] = useState('');
  const [diff, setDiff] = useState(null);
  const [error, setError] = useState('');
  useEffect(() => {
    setError('');
    apiClient.diffOntologyVersion(version.id, against || null).then(setDiff).catch((err) => { setDiff(null); setError(ontologyError(err, t('ontology.loadError'))); });
  }, [apiClient, version.id, against, t]);
  const groups = diff ? [
    ['addedNodeTypes', 'removedNodeTypes', 'changedNodeTypes', 'addedEdgeTypes', 'removedEdgeTypes', 'changedEdgeTypes'],
    ['addedRules', 'removedRules', 'addedConcepts', 'removedConcepts', 'changedConcepts']
  ].flat().filter((k) => diff[k]?.length) : [];
  return (
    <Modal title={t('ontology.diffTitle', { number: version.versionNumber })} size="lg" onClose={onClose}
      footer={<button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>}>
      <div className="form-group">
        <label htmlFor="onto-diff-against">{t('ontology.compareWith')}</label>
        <select id="onto-diff-against" value={against} onChange={(e) => setAgainst(e.target.value)}>
          <option value="">{t('ontology.basedOnDefault')}</option>
          {versions.filter((v) => v.id !== version.id).map((v) => <option key={v.id} value={v.id}>{t('ontology.versionTitle', { number: v.versionNumber })}</option>)}
        </select>
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
      {diff && (
        <div className="onto-diff">
          {groups.length === 0 && !diff.guidanceChanged && !diff.undeclaredTypeActionChanged && <p className="empty-message">{t('ontology.noDifferences')}</p>}
          {diff.guidanceChanged && <p>{t('ontology.guidanceChanged')}</p>}
          {diff.undeclaredTypeActionChanged && <p>{t('ontology.undeclaredChanged')}</p>}
          {groups.map((k) => (
            <div key={k}>
              <h4>{t(`ontology.diff.${k}`)} ({diff[k].length})</h4>
              <ul>{diff[k].slice(0, 200).map((item) => <li key={item}>{item}</li>)}</ul>
            </div>
          ))}
        </div>
      )}
    </Modal>
  );
}

// Download a version as OWL + SKOS (Turtle or JSON-LD).
export function ExportVersionModal({ ontology, version, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [format, setFormat] = useState('turtle');
  const { busy, error, run } = useAction(onClose);
  const go = () => run(async () => {
    const text = await apiClient.exportOntologyVersion(version.id, format);
    const file = FORMAT_FILES[format];
    const safe = (ontology.name || 'ontology').replace(/[^a-z0-9]+/gi, '-').toLowerCase();
    downloadText(`${safe}-v${version.versionNumber}.${file.ext}`, exportText(format, text), file.mime);
  });
  return (
    <Modal title={t('ontology.exportTitle', { number: version.versionNumber })} size="sm" onClose={busy ? () => {} : onClose}
      footer={<ActionFooter busy={busy} onClose={onClose} onGo={go} label={t('ontology.download')} />}>
      <p className="field-hint">{t('ontology.exportHint')}</p>
      <div className="form-group">
        <label htmlFor="onto-e-format">{t('ontology.format')}</label>
        <select id="onto-e-format" value={format} onChange={(e) => setFormat(e.target.value)}>
          <option value="turtle">Turtle</option>
          <option value="jsonld">JSON-LD</option>
        </select>
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
    </Modal>
  );
}
