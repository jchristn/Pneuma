import { useTranslation } from 'react-i18next';

// Editable list of declared node or edge types ({ name, description }).
function TypeListEditor({ items, onChange, readOnly, placeholder }) {
  const { t } = useTranslation();
  const update = (index, patch) => onChange(items.map((item, i) => (i === index ? { ...item, ...patch } : item)));
  const remove = (index) => onChange(items.filter((_, i) => i !== index));
  return (
    <div>
      <div className="onto-scroll">
        <table className="onto-edit-table">
          <thead>
            <tr>
              <th>{t('ontology.typeName')}</th>
              <th>{t('ontology.typeDescription')}</th>
              {!readOnly && <th />}
            </tr>
          </thead>
          <tbody>
            {items.length === 0 && (
              <tr><td colSpan={3} className="empty-message">{t('ontology.noTypes')}</td></tr>
            )}
            {items.map((item, index) => (
              <tr key={index}>
                <td>
                  <input value={item.name || ''} disabled={readOnly} placeholder={placeholder} aria-label={t('ontology.typeName')}
                    onChange={(e) => update(index, { name: e.target.value })} />
                </td>
                <td>
                  <input value={item.description || ''} disabled={readOnly} aria-label={t('ontology.typeDescription')}
                    onChange={(e) => update(index, { description: e.target.value || null })} />
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
      {!readOnly && (
        <div className="onto-toolbar">
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => onChange([...items, { name: '', description: null }])}>
            {t('ontology.addType')}
          </button>
        </div>
      )}
    </div>
  );
}

export default TypeListEditor;
