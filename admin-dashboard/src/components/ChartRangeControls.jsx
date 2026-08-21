import { useTranslation } from 'react-i18next';
import { RANGES } from './ActivityChart';

/**
 * Top-level time-range selector shared by the activity charts on the Home page. Owns no state; the parent
 * holds the selected range id and feeds it to every chart so they stay in lockstep.
 */
function ChartRangeControls({ value, onChange }) {
  const { t } = useTranslation();
  return (
    <div className="segmented" role="tablist" aria-label={t('chart.rangeLabel', 'Chart time range')}>
      {Object.keys(RANGES).map((id) => (
        <button
          key={id}
          type="button"
          role="tab"
          aria-selected={value === id}
          className={value === id ? 'active' : ''}
          onClick={() => onChange(id)}
          title={t('chart.rangeHint', { range: t(RANGES[id].labelKey).toLowerCase(), defaultValue: `Show activity over the last ${t(RANGES[id].labelKey).toLowerCase()}.` })}
        >
          {t(RANGES[id].labelKey)}
        </button>
      ))}
    </div>
  );
}

export default ChartRangeControls;
