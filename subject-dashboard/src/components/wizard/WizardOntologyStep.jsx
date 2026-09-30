import { ontologyCoverage, parseNumbers, questionsKey } from './wizardDraft';
import { GuidanceBar, LockButton } from './WizardParts';

// Step 4: node and relationship types drafted to answer the questions. Editing a type locks it (kept on
// regeneration). The warnings tie the graph back to the questions: what nobody can answer, and what nothing needs.
function WizardOntologyStep({ state, update, updateDraft, generate, busy, t }) {
  const { draft } = state;
  const ontology = draft.ontology || { nodeTypes: [], edgeTypes: [], guidance: '' };
  const questions = draft.questions;
  const { unserved, unused } = ontologyCoverage(ontology, questions);
  const stale = draft.ontology && state.ontologyQuestionsKey && state.ontologyQuestionsKey !== questionsKey(questions);
  const nodeNames = ontology.nodeTypes.map((n) => n.name).filter(Boolean);

  const setOntology = (patch) => updateDraft({ ontology: { ...ontology, ...patch } });
  const changeNode = (i, patch) => setOntology({ nodeTypes: ontology.nodeTypes.map((n, j) => (j === i ? { ...n, locked: true, ...patch } : n)) });
  const changeEdge = (i, patch) => setOntology({ edgeTypes: ontology.edgeTypes.map((e, j) => (j === i ? { ...e, locked: true, ...patch } : e)) });
  const regenerate = (guidance) => {
    update((s) => ({ ...s, ontologyQuestionsKey: questionsKey(s.draft.questions) }));
    generate('ontology', { guidance });
  };

  return (
    <section className="sw-step">
      <h3>{t('wizard.ontology.title')}</h3>
      <p className="sw-hint">{t('wizard.ontology.intro')}</p>
      {stale && <div className="sw-alert sw-alert-info">{t('wizard.ontology.stale')}</div>}
      {!busy && ontology.nodeTypes.length === 0 && <div className="sw-alert sw-alert-info">{t('wizard.ontology.empty')}</div>}
      {draft.ontology && (unserved.length > 0 || unused.length > 0) && (
        <div className="sw-alert sw-alert-warning">
          {unserved.length > 0 && (
            <div>
              <strong>{t('wizard.ontology.unserved')}</strong>
              <ul>{unserved.map((n) => <li key={n}>{n}. {questions[n - 1]?.question}</li>)}</ul>
            </div>
          )}
          {unused.length > 0 && <div><strong>{t('wizard.ontology.unused')}</strong> {unused.join(', ')}</div>}
        </div>
      )}

      <h4>{t('wizard.ontology.nodes')}</h4>
      <div className="sw-table-wrap">
        <table className="sw-table">
          <thead><tr><th className="sw-col-name">{t('wizard.ontology.name')}</th><th>{t('wizard.ontology.description')}</th><th className="sw-col-serves" title={t('wizard.ontology.servesTip')}>{t('wizard.ontology.serves')}</th><th /></tr></thead>
          <tbody>
            {ontology.nodeTypes.map((n, i) => (
              <tr key={i} className={n.locked ? 'locked' : ''}>
                <td><input type="text" value={n.name} maxLength={128} onChange={(e) => changeNode(i, { name: e.target.value })} disabled={!!busy} aria-label={t('wizard.ontology.name')} /></td>
                <td><input type="text" value={n.description || ''} maxLength={500} onChange={(e) => changeNode(i, { description: e.target.value })} disabled={!!busy} aria-label={t('wizard.ontology.description')} /></td>
                <td><input type="text" className="sw-narrow" value={(n.questions || []).join(', ')} onChange={(e) => changeNode(i, { questions: parseNumbers(e.target.value) })} disabled={!!busy} aria-label={t('wizard.ontology.serves')} /></td>
                <td className="sw-actions">
                  <LockButton t={t} locked={!!n.locked} onToggle={() => setOntology({ nodeTypes: ontology.nodeTypes.map((x, j) => (j === i ? { ...x, locked: !x.locked } : x)) })} disabled={!!busy} />
                  <button type="button" className="sw-icon-btn" onClick={() => setOntology({ nodeTypes: ontology.nodeTypes.filter((_, j) => j !== i) })} disabled={!!busy} aria-label={t('wizard.remove')}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <button type="button" className="sw-btn sw-btn-secondary sw-btn-sm" disabled={!!busy}
        onClick={() => setOntology({ nodeTypes: [...ontology.nodeTypes, { name: '', description: '', questions: [], locked: true }] })}>{t('wizard.ontology.addNode')}</button>

      <h4>{t('wizard.ontology.edges')}</h4>
      <div className="sw-table-wrap">
        <table className="sw-table">
          <thead><tr><th className="sw-col-name">{t('wizard.ontology.name')}</th><th className="sw-col-end">{t('wizard.ontology.from')}</th><th className="sw-col-end">{t('wizard.ontology.to')}</th><th>{t('wizard.ontology.description')}</th><th className="sw-col-serves" title={t('wizard.ontology.servesTip')}>{t('wizard.ontology.serves')}</th><th /></tr></thead>
          <tbody>
            {ontology.edgeTypes.map((e, i) => (
              <tr key={i} className={e.locked ? 'locked' : ''}>
                <td><input type="text" value={e.name} maxLength={128} onChange={(ev) => changeEdge(i, { name: ev.target.value })} disabled={!!busy} aria-label={t('wizard.ontology.name')} /></td>
                <td>
                  <select value={e.from || ''} onChange={(ev) => changeEdge(i, { from: ev.target.value || null })} disabled={!!busy} aria-label={t('wizard.ontology.from')}>
                    <option value="">{t('wizard.ontology.any')}</option>
                    {nodeNames.map((name) => <option key={name} value={name}>{name}</option>)}
                  </select>
                </td>
                <td>
                  <select value={e.to || ''} onChange={(ev) => changeEdge(i, { to: ev.target.value || null })} disabled={!!busy} aria-label={t('wizard.ontology.to')}>
                    <option value="">{t('wizard.ontology.any')}</option>
                    {nodeNames.map((name) => <option key={name} value={name}>{name}</option>)}
                  </select>
                </td>
                <td><input type="text" value={e.description || ''} maxLength={500} onChange={(ev) => changeEdge(i, { description: ev.target.value })} disabled={!!busy} aria-label={t('wizard.ontology.description')} /></td>
                <td><input type="text" className="sw-narrow" value={(e.questions || []).join(', ')} onChange={(ev) => changeEdge(i, { questions: parseNumbers(ev.target.value) })} disabled={!!busy} aria-label={t('wizard.ontology.serves')} /></td>
                <td className="sw-actions">
                  <LockButton t={t} locked={!!e.locked} onToggle={() => setOntology({ edgeTypes: ontology.edgeTypes.map((x, j) => (j === i ? { ...x, locked: !x.locked } : x)) })} disabled={!!busy} />
                  <button type="button" className="sw-icon-btn" onClick={() => setOntology({ edgeTypes: ontology.edgeTypes.filter((_, j) => j !== i) })} disabled={!!busy} aria-label={t('wizard.remove')}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <button type="button" className="sw-btn sw-btn-secondary sw-btn-sm" disabled={!!busy}
        onClick={() => setOntology({ edgeTypes: [...ontology.edgeTypes, { name: '', description: '', from: null, to: null, questions: [], locked: true }] })}>{t('wizard.ontology.addEdge')}</button>

      <label className="sw-label" htmlFor="sw-guidance-text">{t('wizard.ontology.guidance')}</label>
      <textarea id="sw-guidance-text" rows={3} maxLength={2000} value={ontology.guidance || ''} onChange={(e) => setOntology({ guidance: e.target.value })} disabled={!!busy} />
      <GuidanceBar t={t} busy={busy} regenerateLabel={ontology.nodeTypes.length === 0 ? t('wizard.ontology.draft') : t('wizard.regenerateRest')} onRegenerate={regenerate} placeholder={t('wizard.ontology.guidancePlaceholder')} />
    </section>
  );
}

export default WizardOntologyStep;
