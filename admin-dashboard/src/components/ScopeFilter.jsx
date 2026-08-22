import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import LabelTagEditor from './LabelTagEditor';

function FunnelIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
    </svg>
  );
}

/** Count the labels/tags that carry a value (so the toggle can show how many predicates are active). */
export function scopeActiveCount(labels, tags) {
  const l = (Array.isArray(labels) ? labels : []).filter((x) => (x || '').trim()).length;
  const tg = (Array.isArray(tags) ? tags : []).filter((x) => (x?.key || '').trim()).length;
  return l + tg;
}

/**
 * Build a RetrievalFilter payload (required labels + required tags) from the scope editor's arrays, or null
 * when nothing is set. Tags use the Equals condition — the common "only include content carrying this" case.
 */
export function buildScopeFilter(labels, tags) {
  const requiredLabels = [];
  const seen = new Set();
  for (const raw of Array.isArray(labels) ? labels : []) {
    const v = (raw || '').trim();
    if (v && !seen.has(v)) { seen.add(v); requiredLabels.push(v); }
  }
  const requiredTags = [];
  for (const t of Array.isArray(tags) ? tags : []) {
    const k = (t?.key || '').trim();
    if (k) requiredTags.push({ key: k, condition: 'Equals', value: (t?.value || '').trim() });
  }
  if (requiredLabels.length === 0 && requiredTags.length === 0) return null;
  return { requiredLabels, excludedLabels: [], requiredTags, excludedTags: [] };
}

/**
 * A compact, collapsible "scope" control that narrows retrieval to content ingested with the chosen labels
 * and tags. `labels` is a string[]; `tags` is a {key,value}[]; `onChange` receives the updated `{ labels, tags }`.
 */
export default function ScopeFilter({ labels, tags, onChange, disabled = false }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const active = scopeActiveCount(labels, tags);

  return (
    <div className={`scope-filter${open ? ' open' : ''}`}>
      <button
        type="button"
        className={`scope-toggle${active > 0 ? ' has-active' : ''}`}
        onClick={() => setOpen((o) => !o)}
        disabled={disabled}
        aria-expanded={open}
      >
        <FunnelIcon />
        <span>{t('ask.scope', 'Scope')}</span>
        {active > 0 ? <span className="scope-badge">{active}</span> : null}
      </button>
      {open ? (
        <div className="scope-panel">
          <p className="scope-hint">{t('ask.scopeHint', 'Limit answers to content ingested with these labels and tags.')}</p>
          <LabelTagEditor labels={labels} tags={tags} onChange={onChange} />
          {active > 0 ? (
            <button type="button" className="scope-clear" onClick={() => onChange({ labels: [], tags: [] })}>
              {t('ask.scopeClear', 'Clear scope')}
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
