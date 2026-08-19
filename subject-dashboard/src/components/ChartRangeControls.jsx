import { ACTIVITY_RANGES } from '../utils/activity';

/**
 * Segmented range control (Hour/Day/Week/Month) plus a manual refresh button
 * for activity charts.
 */
function ChartRangeControls({ value, onChange, onRefresh, loading = false }) {
  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      <div className="segmented" role="tablist" aria-label="Chart range">
        {ACTIVITY_RANGES.map((r) => (
          <button
            key={r.id}
            type="button"
            role="tab"
            aria-selected={value === r.id}
            className={`segmented-btn ${value === r.id ? 'active' : ''}`}
            onClick={() => onChange(r.id)}
          >
            {r.labelKey}
          </button>
        ))}
      </div>
      {onRefresh && (
        <button className="btn-icon" onClick={onRefresh} disabled={loading} title="Refresh">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            style={loading ? { animation: 'spin 0.8s linear infinite' } : undefined}
          >
            <path d="M23 4v6h-6" />
            <path d="M1 20v-6h6" />
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
          </svg>
        </button>
      )}
    </div>
  );
}

export default ChartRangeControls;
