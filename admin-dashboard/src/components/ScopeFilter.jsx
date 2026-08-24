import { useState, useRef, useEffect } from 'react';
import { createPortal } from 'react-dom';
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
export default function ScopeFilter({ labels, tags, onChange, disabled = false, compact = false }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const active = scopeActiveCount(labels, tags);
  const rootRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('keydown', onKey);
    // Only the non-compact dropdown closes on an outside click; the compact modal manages its own dismissal
    // (backdrop click / close / Escape), and its panel is portaled outside this element's subtree.
    let onDoc = null;
    if (!compact) {
      onDoc = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
      document.addEventListener('mousedown', onDoc);
    }
    return () => { document.removeEventListener('keydown', onKey); if (onDoc) document.removeEventListener('mousedown', onDoc); };
  }, [open, compact]);

  const editor = (
    <>
      <p className="scope-hint">{t('ask.scopeHint', 'Limit answers to content ingested with these labels and tags.')}</p>
      <LabelTagEditor labels={labels} tags={tags} onChange={onChange} />
      {active > 0 ? (
        <button type="button" className="scope-clear" onClick={() => onChange({ labels: [], tags: [] })}>
          {t('ask.scopeClear', 'Clear scope')}
        </button>
      ) : null}
    </>
  );

  return (
    <div className={`scope-filter${compact ? ' scope-compact' : ''}${open ? ' open' : ''}`} ref={rootRef}>
      {compact ? (
        <button
          type="button"
          className={`chat-send scope-icon-btn${active > 0 ? ' has-active' : ''}`}
          onClick={() => setOpen((o) => !o)}
          disabled={disabled}
          aria-expanded={open}
          aria-label={t('ask.scope', 'Scope')}
          title={t('ask.scope', 'Scope')}
        >
          <FunnelIcon />
          {active > 0 ? <span className="scope-dot" aria-hidden="true" /> : null}
        </button>
      ) : (
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
      )}
      {/* Non-compact (hero): an inline dropdown. */}
      {open && !compact ? <div className="scope-panel">{editor}</div> : null}
      {/* Compact (composer): a standalone modal, portaled to the body so it is never clipped by the composer card. */}
      {open && compact
        ? createPortal(
            <div className="scope-modal-overlay" role="presentation" onMouseDown={(e) => { if (e.target === e.currentTarget) setOpen(false); }}>
              <div className="scope-modal" role="dialog" aria-modal="true" aria-label={t('ask.scope', 'Scope')}>
                <div className="scope-modal-head">
                  <span className="scope-modal-title">{t('ask.scopeTitle', 'Scope retrieval')}</span>
                  <button type="button" className="scope-modal-close" onClick={() => setOpen(false)} aria-label={t('common.close', 'Close')}>✕</button>
                </div>
                {editor}
                <div className="scope-modal-actions">
                  <button type="button" className="scope-modal-done" onClick={() => setOpen(false)}>{t('common.done', 'Done')}</button>
                </div>
              </div>
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}
