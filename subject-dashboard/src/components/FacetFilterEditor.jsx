import { useState, useEffect, useRef } from 'react';

// Structured editor for a retrieval filter (labels + tags), replacing a raw-JSON textbox. Labels are a
// List<string> (one textbox per row); tags are key/value pairs (two textboxes per row). Each row has a delete
// icon; the last row has an add icon. The value is the serialized RetrievalFilter JSON the backend expects.

function TrashIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}
function PlusIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  );
}

function parse(value) {
  const base = { requiredLabels: [], excludedLabels: [], requiredTags: [], excludedTags: [] };
  try {
    const o = value ? JSON.parse(value) : null;
    if (o) {
      base.requiredLabels = Array.isArray(o.requiredLabels) ? o.requiredLabels.slice() : [];
      base.excludedLabels = Array.isArray(o.excludedLabels) ? o.excludedLabels.slice() : [];
      base.requiredTags = Array.isArray(o.requiredTags) ? o.requiredTags.map((t) => ({ key: t.key || '', value: t.value || '' })) : [];
      base.excludedTags = Array.isArray(o.excludedTags) ? o.excludedTags.map((t) => ({ key: t.key || '', value: t.value || '' })) : [];
    }
  } catch { /* not valid JSON yet — start empty */ }
  return base;
}

function serialize(s) {
  const labels = (a) => a.map((x) => (x || '').trim()).filter(Boolean);
  const tags = (a) => a.filter((t) => (t.key || '').trim()).map((t) => ({ key: t.key.trim(), condition: 'Equals', value: (t.value || '').trim() }));
  const out = { requiredLabels: labels(s.requiredLabels), excludedLabels: labels(s.excludedLabels), requiredTags: tags(s.requiredTags), excludedTags: tags(s.excludedTags) };
  const empty = !out.requiredLabels.length && !out.excludedLabels.length && !out.requiredTags.length && !out.excludedTags.length;
  return empty ? '' : JSON.stringify(out);
}

function LabelList({ label, hint, items, onItems, placeholder }) {
  return (
    <div className="ff-group">
      <div className="ff-group-label" title={hint}>{label}</div>
      {items.map((v, i) => (
        <div className="ff-row" key={i}>
          <input className="ff-input" value={v} placeholder={placeholder} onChange={(e) => { const n = items.slice(); n[i] = e.target.value; onItems(n); }} />
          <button type="button" className="ff-icon ff-del" title="Remove" aria-label="Remove" onClick={() => onItems(items.filter((_, idx) => idx !== i))}><TrashIcon /></button>
          {i === items.length - 1 && <button type="button" className="ff-icon ff-add" title="Add" aria-label="Add" onClick={() => onItems([...items, ''])}><PlusIcon /></button>}
        </div>
      ))}
      {items.length === 0 && <button type="button" className="ff-icon ff-add ff-add-empty" title="Add" aria-label="Add" onClick={() => onItems([''])}><PlusIcon /></button>}
    </div>
  );
}

function TagList({ label, hint, items, onItems }) {
  return (
    <div className="ff-group">
      <div className="ff-group-label" title={hint}>{label}</div>
      {items.map((t, i) => (
        <div className="ff-row" key={i}>
          <input className="ff-input" value={t.key} placeholder="key" onChange={(e) => { const n = items.slice(); n[i] = { ...n[i], key: e.target.value }; onItems(n); }} />
          <input className="ff-input" value={t.value} placeholder="value" onChange={(e) => { const n = items.slice(); n[i] = { ...n[i], value: e.target.value }; onItems(n); }} />
          <button type="button" className="ff-icon ff-del" title="Remove" aria-label="Remove" onClick={() => onItems(items.filter((_, idx) => idx !== i))}><TrashIcon /></button>
          {i === items.length - 1 && <button type="button" className="ff-icon ff-add" title="Add" aria-label="Add" onClick={() => onItems([...items, { key: '', value: '' }])}><PlusIcon /></button>}
        </div>
      ))}
      {items.length === 0 && <button type="button" className="ff-icon ff-add ff-add-empty" title="Add" aria-label="Add" onClick={() => onItems([{ key: '', value: '' }])}><PlusIcon /></button>}
    </div>
  );
}

/**
 * Structured retrieval-filter editor. `value` is the serialized RetrievalFilter JSON; `onChange` receives the
 * updated JSON string ('' when the filter is empty).
 */
export default function FacetFilterEditor({ value, onChange }) {
  const [state, setState] = useState(() => parse(value));
  const lastEmit = useRef(null);
  useEffect(() => { if (value !== lastEmit.current) setState(parse(value)); }, [value]);
  const update = (patch) => {
    const next = { ...state, ...patch };
    setState(next);
    const json = serialize(next);
    lastEmit.current = json;
    onChange(json);
  };
  return (
    <div className="ff-editor">
      <LabelList label="Required labels" hint="A chunk must carry every one of these labels to be eligible." items={state.requiredLabels} placeholder="label" onItems={(v) => update({ requiredLabels: v })} />
      <LabelList label="Excluded labels" hint="A chunk carrying any of these labels is excluded." items={state.excludedLabels} placeholder="label" onItems={(v) => update({ excludedLabels: v })} />
      <TagList label="Required tags" hint="A chunk must carry every one of these tag key/value pairs." items={state.requiredTags} onItems={(v) => update({ requiredTags: v })} />
      <TagList label="Excluded tags" hint="A chunk carrying any of these tag key/value pairs is excluded." items={state.excludedTags} onItems={(v) => update({ excludedTags: v })} />
    </div>
  );
}
