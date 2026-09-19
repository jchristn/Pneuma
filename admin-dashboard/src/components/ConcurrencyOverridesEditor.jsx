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
    <div className="concurrency-overrides">
      <button
        type="button"
        className="button-secondary"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
      >
        {open ? `▾ ${t('common.hide')}` : `▸ ${t('common.show')}`}{setCount > 0 ? ` (${setCount})` : ''}
      </button>
      {open && (
        <div style={{ marginTop: '0.75rem' }}>
          <p className="field-hint" style={{ marginTop: 0 }}>{t('subjects.concurrencyOverridesHint')}</p>
          {INGESTION_TUNABLE_GROUPS.map((group) => (
            <fieldset className="settings-subgroup" key={group.key}>
              <legend>{t(`processing.groups.${group.key}`)}</legend>
              <div className="settings-field-grid">
                {group.fields.map((key) => {
                  const tip = t(`processing.tips.${key}`);
                  const def = defaults ? defaults[key] : undefined;
                  const placeholder = def !== undefined && def !== null
                    ? t('subjects.overrideDefault', { value: def })
                    : t('subjects.overrideInherit');
                  const raw = current[key];
                  return (
                    <div className="field" key={key}>
                      <label htmlFor={`ovr-${key}`} className="has-tip" title={tip}>{t(`processing.fields.${key}`)}</label>
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
              </div>
            </fieldset>
          ))}
        </div>
      )}
    </div>
  );
}

export default ConcurrencyOverridesEditor;
