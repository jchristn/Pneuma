import { useTranslation } from 'react-i18next';

// Editor for the plain labels + tags attached to ingested content (as opposed to FacetFilterEditor, which
// edits a required/excluded retrieval filter). Labels are a list of strings (one textbox per row); tags are
// key/value pairs (two textboxes per row). Shares the .ff-* styling with FacetFilterEditor so the two read as
// one system. Controlled via `labels` (string[]) and `tags` ({key,value}[]); `onChange` receives { labels, tags }.

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

function LabelList({ label, hint, items, onItems, placeholder, addLabel, removeLabel }) {
  return (
    <div className="ff-group">
      <div className="ff-group-label" title={hint}>{label}</div>
      {items.map((v, i) => (
        <div className="ff-row" key={i}>
          <input className="ff-input" value={v} placeholder={placeholder} onChange={(e) => { const n = items.slice(); n[i] = e.target.value; onItems(n); }} />
          <button type="button" className="ff-icon ff-del" title={removeLabel} aria-label={removeLabel} onClick={() => onItems(items.filter((_, idx) => idx !== i))}><TrashIcon /></button>
          {i === items.length - 1 && <button type="button" className="ff-icon ff-add" title={addLabel} aria-label={addLabel} onClick={() => onItems([...items, ''])}><PlusIcon /></button>}
        </div>
      ))}
      {items.length === 0 && <button type="button" className="ff-icon ff-add ff-add-empty" title={addLabel} aria-label={addLabel} onClick={() => onItems([''])}><PlusIcon /></button>}
    </div>
  );
}

function TagList({ label, hint, items, onItems, keyPlaceholder, valuePlaceholder, addLabel, removeLabel }) {
  return (
    <div className="ff-group">
      <div className="ff-group-label" title={hint}>{label}</div>
      {items.map((t, i) => (
        <div className="ff-row" key={i}>
          <input className="ff-input" value={t.key} placeholder={keyPlaceholder} onChange={(e) => { const n = items.slice(); n[i] = { ...n[i], key: e.target.value }; onItems(n); }} />
          <input className="ff-input" value={t.value} placeholder={valuePlaceholder} onChange={(e) => { const n = items.slice(); n[i] = { ...n[i], value: e.target.value }; onItems(n); }} />
          <button type="button" className="ff-icon ff-del" title={removeLabel} aria-label={removeLabel} onClick={() => onItems(items.filter((_, idx) => idx !== i))}><TrashIcon /></button>
          {i === items.length - 1 && <button type="button" className="ff-icon ff-add" title={addLabel} aria-label={addLabel} onClick={() => onItems([...items, { key: '', value: '' }])}><PlusIcon /></button>}
        </div>
      ))}
      {items.length === 0 && <button type="button" className="ff-icon ff-add ff-add-empty" title={addLabel} aria-label={addLabel} onClick={() => onItems([{ key: '', value: '' }])}><PlusIcon /></button>}
    </div>
  );
}

/**
 * Labels + tags editor for content ingestion. `labels` is a string[]; `tags` is a {key,value}[]. `onChange`
 * receives the updated `{ labels, tags }`.
 */
export default function LabelTagEditor({ labels, tags, onChange }) {
  const { t } = useTranslation();
  const safeLabels = Array.isArray(labels) ? labels : [];
  const safeTags = Array.isArray(tags) ? tags : [];
  const addLabel = t('common.add', 'Add');
  const removeLabel = t('common.remove', 'Remove');
  return (
    <div className="ff-editor">
      <LabelList
        label={t('links.labels', 'Labels')}
        hint={t('links.labelsHint', 'Labels attached to every chunk this content produces; retrieval can be scoped to them.')}
        items={safeLabels}
        placeholder={t('links.labelPlaceholder', 'label')}
        addLabel={addLabel}
        removeLabel={removeLabel}
        onItems={(v) => onChange({ labels: v, tags: safeTags })}
      />
      <TagList
        label={t('links.tags', 'Tags')}
        hint={t('links.tagsHint', 'Key/value tags attached to every chunk this content produces; retrieval can be scoped to them.')}
        items={safeTags}
        keyPlaceholder={t('links.tagKeyPlaceholder', 'key')}
        valuePlaceholder={t('links.tagValuePlaceholder', 'value')}
        addLabel={addLabel}
        removeLabel={removeLabel}
        onItems={(v) => onChange({ labels: safeLabels, tags: v })}
      />
    </div>
  );
}

/**
 * Convert the editor's `{ labels, tags }` into the request payload fields:
 * `labels` (trimmed, de-duplicated, non-empty) and `tags` (a { key: value } map, blank keys dropped).
 */
export function toLabelTagPayload(labels, tags) {
  const outLabels = [];
  const seen = new Set();
  for (const raw of Array.isArray(labels) ? labels : []) {
    const v = (raw || '').trim();
    if (v && !seen.has(v)) { seen.add(v); outLabels.push(v); }
  }
  const outTags = {};
  for (const t of Array.isArray(tags) ? tags : []) {
    const k = (t?.key || '').trim();
    if (k) outTags[k] = (t?.value || '').trim();
  }
  return { labels: outLabels, tags: outTags };
}
