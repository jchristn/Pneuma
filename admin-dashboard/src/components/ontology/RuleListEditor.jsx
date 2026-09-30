import { useTranslation } from 'react-i18next';
import { RULE_TYPES, RULE_ACTIONS, NODE_FIELDS, RULE_FIELDS, newRule } from './ontologyUtils';

// A type-name input backed by a datalist of the version's declared types, so any name can still be typed.
function TypeInput({ value, onChange, listId, disabled, label }) {
  return (
    <input value={value || ''} list={listId} disabled={disabled} aria-label={label} placeholder={label}
      onChange={(e) => onChange(e.target.value || null)} />
  );
}

// Editable list of constraint rules. Each rule shows only the fields its type uses.
function RuleListEditor({ rules, onChange, readOnly, nodeTypes, edgeTypes }) {
  const { t } = useTranslation();
  const update = (index, patch) => onChange(rules.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const remove = (index) => onChange(rules.filter((_, i) => i !== index));

  const fieldCell = (rule, index, name) => {
    switch (name) {
      case 'nodeType':
      case 'fromNodeType':
      case 'toNodeType':
        return <TypeInput value={rule[name]} listId="onto-node-types" disabled={readOnly} label={t(`ontology.rule.${name}`)}
          onChange={(v) => update(index, { [name]: v })} />;
      case 'edgeType':
        return <TypeInput value={rule.edgeType} listId="onto-edge-types" disabled={readOnly} label={t('ontology.rule.edgeType')}
          onChange={(v) => update(index, { edgeType: v })} />;
      case 'field':
        return (
          <select value={rule.field || ''} disabled={readOnly} aria-label={t('ontology.rule.field')} onChange={(e) => update(index, { field: e.target.value || null })}>
            <option value="">{t('ontology.rule.field')}</option>
            {NODE_FIELDS.map((f) => <option key={f} value={f}>{t(`ontology.nodeField.${f}`)}</option>)}
          </select>
        );
      case 'pattern':
        return <input value={rule.pattern || ''} disabled={readOnly} placeholder={t('ontology.rule.pattern')} aria-label={t('ontology.rule.pattern')}
          onChange={(e) => update(index, { pattern: e.target.value || null })} />;
      case 'maxCount':
        return <input type="number" min={0} max={100000} value={rule.maxCount ?? 1} disabled={readOnly} aria-label={t('ontology.rule.maxCount')}
          title={t('ontology.rule.maxCount')} onChange={(e) => update(index, { maxCount: Number(e.target.value) })} />;
      case 'minConfidence':
        return <input type="number" min={0} max={1} step={0.05} value={rule.minConfidence ?? 0.5} disabled={readOnly} aria-label={t('ontology.rule.minConfidence')}
          title={t('ontology.rule.minConfidence')} onChange={(e) => update(index, { minConfidence: Number(e.target.value) })} />;
      default:
        return null;
    }
  };

  const changeType = (index, rule, ruleType) => {
    const action = ruleType !== 'EdgeEndpoints' && rule.action === 'Reverse' ? 'Warn' : rule.action;
    update(index, { ruleType, action });
  };

  return (
    <div>
      <datalist id="onto-node-types">{nodeTypes.map((n) => <option key={n.name} value={n.name} />)}</datalist>
      <datalist id="onto-edge-types">{edgeTypes.map((n) => <option key={n.name} value={n.name} />)}</datalist>
      <p className="field-hint">{t('ontology.rulesHint')}</p>
      <div className="onto-scroll">
        <table className="onto-edit-table">
          <thead>
            <tr>
              <th>{t('ontology.rule.type')}</th>
              <th colSpan={3}>{t('ontology.rule.match')}</th>
              <th>{t('ontology.rule.action')}</th>
              <th>{t('ontology.typeDescription')}</th>
              {!readOnly && <th />}
            </tr>
          </thead>
          <tbody>
            {rules.length === 0 && <tr><td colSpan={7} className="empty-message">{t('ontology.noRules')}</td></tr>}
            {rules.map((rule, index) => {
              const fields = RULE_FIELDS[rule.ruleType] || [];
              return (
                <tr key={rule.id || `new-${index}`}>
                  <td>
                    <select value={rule.ruleType} disabled={readOnly} aria-label={t('ontology.rule.type')} title={t(`ontology.ruleTypeTip.${rule.ruleType}`)}
                      onChange={(e) => changeType(index, rule, e.target.value)}>
                      {RULE_TYPES.map((rt) => <option key={rt} value={rt}>{t(`ontology.ruleType.${rt}`)}</option>)}
                    </select>
                  </td>
                  {[0, 1, 2].map((slot) => <td key={slot}>{fields[slot] ? fieldCell(rule, index, fields[slot]) : null}</td>)}
                  <td>
                    <select value={rule.action} disabled={readOnly} aria-label={t('ontology.rule.action')} title={t(`ontology.ruleActionTip.${rule.action}`)}
                      onChange={(e) => update(index, { action: e.target.value })}>
                      {RULE_ACTIONS.filter((a) => a !== 'Reverse' || rule.ruleType === 'EdgeEndpoints')
                        .map((a) => <option key={a} value={a}>{t(`ontology.ruleAction.${a}`)}</option>)}
                    </select>
                  </td>
                  <td>
                    <input value={rule.description || ''} disabled={readOnly} aria-label={t('ontology.typeDescription')}
                      onChange={(e) => update(index, { description: e.target.value || null })} />
                  </td>
                  {!readOnly && (
                    <td className="onto-narrow">
                      <button type="button" className="icon-button" onClick={() => remove(index)} aria-label={t('common.delete')} title={t('common.delete')}>✕</button>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {!readOnly && (
        <div className="onto-toolbar">
          <button type="button" className="button-secondary button-small" onClick={() => onChange([...rules, newRule()])}>{t('ontology.addRule')}</button>
        </div>
      )}
    </div>
  );
}

export default RuleListEditor;
