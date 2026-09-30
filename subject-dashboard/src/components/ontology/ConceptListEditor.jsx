import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { newConcept } from './ontologyUtils';

const PAGE = 100;

const splitLabels = (text) => text.split(',').map((s) => s.trim()).filter(Boolean);

// Editable taxonomy: concepts with a preferred label, alternative labels, a broader concept, and the node type
// they tag. Large taxonomies (e.g. an imported SKOS scheme) are filtered and shown a page at a time.
function ConceptListEditor({ concepts, onChange, readOnly }) {
  const { t } = useTranslation();
  const [filter, setFilter] = useState('');
  const [limit, setLimit] = useState(PAGE);

  const visible = useMemo(() => {
    const q = filter.trim().toLowerCase();
    const indexed = concepts.map((c, index) => ({ c, index }));
    if (!q) return indexed;
    return indexed.filter(({ c }) => [c.key, c.prefLabel, ...(c.altLabels || [])].some((s) => String(s || '').toLowerCase().includes(q)));
  }, [concepts, filter]);

  const update = (index, patch) => onChange(concepts.map((c, i) => (i === index ? { ...c, ...patch } : c)));
  const remove = (index) => onChange(concepts.filter((_, i) => i !== index));

  return (
    <div>
      <p className="field-hint">{t('ontology.conceptsHint')}</p>
      <div className="onto-toolbar">
        <input type="search" value={filter} placeholder={t('ontology.filterConcepts')} aria-label={t('ontology.filterConcepts')}
          onChange={(e) => { setFilter(e.target.value); setLimit(PAGE); }} />
        <span className="field-hint">{t('ontology.conceptCount', { shown: Math.min(limit, visible.length), total: concepts.length })}</span>
        <span className="onto-spacer" />
        {!readOnly && (
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => { onChange([newConcept(), ...concepts]); setFilter(''); }}>
            {t('ontology.addConcept')}
          </button>
        )}
      </div>
      <datalist id="onto-concept-keys">{concepts.slice(0, 2000).map((c, i) => <option key={i} value={c.key || c.prefLabel} />)}</datalist>
      <div className="onto-scroll">
        <table className="onto-edit-table">
          <thead>
            <tr>
              <th>{t('ontology.concept.prefLabel')}</th>
              <th>{t('ontology.concept.altLabels')}</th>
              <th>{t('ontology.concept.key')}</th>
              <th>{t('ontology.concept.broader')}</th>
              <th>{t('ontology.concept.nodeType')}</th>
              <th title={t('ontology.concept.caseSensitiveTip')}>{t('ontology.concept.caseSensitive')}</th>
              {!readOnly && <th />}
            </tr>
          </thead>
          <tbody>
            {visible.length === 0 && <tr><td colSpan={7} className="empty-message">{t('ontology.noConcepts')}</td></tr>}
            {visible.slice(0, limit).map(({ c, index }) => (
              <tr key={index}>
                <td><input value={c.prefLabel || ''} disabled={readOnly} aria-label={t('ontology.concept.prefLabel')} onChange={(e) => update(index, { prefLabel: e.target.value })} /></td>
                <td>
                  <input key={(c.altLabels || []).join('|')} defaultValue={(c.altLabels || []).join(', ')} disabled={readOnly}
                    aria-label={t('ontology.concept.altLabels')} placeholder={t('ontology.concept.altLabelsHint')}
                    onBlur={(e) => update(index, { altLabels: splitLabels(e.target.value) })} />
                </td>
                <td><input value={c.key || ''} disabled={readOnly} aria-label={t('ontology.concept.key')} placeholder={c.prefLabel} onChange={(e) => update(index, { key: e.target.value })} /></td>
                <td><input value={c.broaderKey || ''} list="onto-concept-keys" disabled={readOnly} aria-label={t('ontology.concept.broader')} onChange={(e) => update(index, { broaderKey: e.target.value || null })} /></td>
                <td><input value={c.nodeType || ''} list="onto-node-types" disabled={readOnly} aria-label={t('ontology.concept.nodeType')} onChange={(e) => update(index, { nodeType: e.target.value })} /></td>
                <td className="onto-narrow">
                  <input type="checkbox" checked={!!c.caseSensitive} disabled={readOnly} aria-label={t('ontology.concept.caseSensitive')}
                    onChange={(e) => update(index, { caseSensitive: e.target.checked })} />
                </td>
                {!readOnly && (
                  <td className="onto-narrow">
                    <button type="button" className="btn-icon" onClick={() => remove(index)} aria-label={t('common.delete')} title={t('common.delete')}>✕</button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {visible.length > limit && (
        <div className="onto-toolbar">
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => setLimit(limit + PAGE)}>{t('ontology.showMore')}</button>
        </div>
      )}
    </div>
  );
}

export default ConceptListEditor;
