import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { INGESTION_TUNABLE_GROUPS } from '../config/ingestionTunables';

// Count how many overrides carry an actual (non-null) value.
function countSet(obj) {
  if (!obj || typeof obj !== 'object') return 0;
  return Object.values(obj).filter((v) => v !== null && v !== undefined && v !== '').length;
}

/**
 * Collapsible, advanced per-subject ingestion concurrency overrides. Each tunable is an OPTIONAL number
 * input; blank means "inherit the system default" (shown as the placeholder). `value` is the subject's
 * `concurrencyOverrides` object (keys nullable); `onChange` receives an object containing only the keys
 * the operator set. `defaults` is the system-default IngestionTuning used to populate placeholders.
 */
function ConcurrencyOverridesEditor({ value, onChange, defaults }) {
  const { t } = useTranslation();
  const current = value && typeof value === 'object' ? value : {};
  const [open, setOpen] = useState(() => countSet(current) > 0);
  const setCount = countSet(current);

  const setField = (key, raw) => {
    const next = { ...current };
    if (raw === '' || raw === null || raw === undefined) delete next[key];
    else next[key] = Number(raw);
    onChange(next);
  };

  return (
    <div className="form-group">
      <button
        type="button"
        className="btn btn-secondary"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
      >
        {open ? '▾' : '▸'} {t('subjects.concurrencyOverrides', 'Concurrency overrides (advanced)')}{setCount > 0 ? ` (${setCount})` : ''}
      </button>
      {open && (
        <div style={{ marginTop: '0.75rem' }}>
          <div className="field-hint">{t('subjects.concurrencyOverridesHint', 'Advanced. Blank fields inherit the system default shown as the placeholder. Only the fields you set are saved as overrides.')}</div>
          {INGESTION_TUNABLE_GROUPS.map((group) => (
            <fieldset key={group.key} style={{ border: '1px solid var(--border, #ddd)', borderRadius: 6, padding: '0.75rem', margin: '0.75rem 0' }}>
              <legend style={{ padding: '0 0.35rem', fontSize: '0.85rem', fontWeight: 600 }}>{t(`processing.groups.${group.key}`)}</legend>
              {group.fields.map((key) => {
                const tip = t(`processing.tips.${key}`);
                const def = defaults ? defaults[key] : undefined;
                const placeholder = def !== undefined && def !== null
                  ? t('subjects.overrideDefault', { value: def, defaultValue: `Default ${def}` })
                  : t('subjects.overrideInherit', 'Inherit default');
                const raw = current[key];
                return (
                  <div className="form-group" key={key} title={tip}>
                    <label htmlFor={`ovr-${key}`} title={tip}>{t(`processing.fields.${key}`)}</label>
                    <input
                      id={`ovr-${key}`}
                      type="number"
                      min="0"
                      value={raw ?? ''}
                      placeholder={placeholder}
                      onChange={(e) => setField(key, e.target.value)}
                      title={tip}
                    />
                  </div>
                );
              })}
            </fieldset>
          ))}
        </div>
      )}
    </div>
  );
}

export default ConcurrencyOverridesEditor;
