import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CopyButton from './CopyButton';
import './JsonViewer.css';

function isExpandable(value) {
  return value !== null && typeof value === 'object';
}

function summarize(value) {
  if (Array.isArray(value)) return `[] ${value.length} item${value.length === 1 ? '' : 's'}`;
  const count = Object.keys(value).length;
  return `{} ${count} key${count === 1 ? '' : 's'}`;
}

function Leaf({ value }) {
  if (value === null) return <span className="json-null">null</span>;
  const type = typeof value;
  if (type === 'string') return <span className="json-string">"{value}"</span>;
  if (type === 'number') return <span className="json-number">{String(value)}</span>;
  if (type === 'boolean') return <span className="json-boolean">{String(value)}</span>;
  return <span className="json-value">{String(value)}</span>;
}

// A single collapsible node in the JSON tree. Objects and arrays render a toggle and, when expanded,
// their children indented beneath; primitives render inline.
function JsonNode({ name, value, depth, defaultExpandDepth }) {
  const expandable = isExpandable(value);
  const [expanded, setExpanded] = useState(depth < defaultExpandDepth);

  const label = name === null ? null : (
    <span className="json-key">{Array.isArray(name) ? name[0] : name}</span>
  );

  if (!expandable) {
    return (
      <div className="json-row" style={{ paddingLeft: `${depth * 1.1}rem` }}>
        {label}{label && <span className="json-colon">: </span>}<Leaf value={value} />
      </div>
    );
  }

  const entries = Array.isArray(value)
    ? value.map((v, i) => [String(i), v])
    : Object.entries(value);

  return (
    <div className="json-node">
      <div
        className="json-row json-toggle"
        style={{ paddingLeft: `${depth * 1.1}rem` }}
        onClick={() => setExpanded((e) => !e)}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setExpanded((x) => !x); } }}
      >
        <span className="json-caret">{expanded ? '▾' : '▸'}</span>
        {label}{label && <span className="json-colon">: </span>}
        <span className="json-summary">{summarize(value)}</span>
      </div>
      {expanded && entries.map(([k, v]) => (
        <JsonNode key={k} name={k} value={v} depth={depth + 1} defaultExpandDepth={defaultExpandDepth} />
      ))}
    </div>
  );
}

export function JsonBlock({ value, copyLabel = 'JSON', defaultExpandDepth = 2 }) {
  // Strings (e.g. a raw source document) are shown as plain text; structured data gets the collapsible tree.
  const isString = typeof value === 'string';
  const copyText = isString ? value : JSON.stringify(value, null, 2);
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.5rem' }}>
        <CopyButton value={copyText} label={copyLabel} />
      </div>
      {isString ? (
        <pre className="code-block">{value}</pre>
      ) : (
        <div className="json-tree">
          <JsonNode name={null} value={value} depth={0} defaultExpandDepth={defaultExpandDepth} />
        </div>
      )}
    </div>
  );
}

function JsonViewer({ title, data, copyLabel, size = 'lg', onClose }) {
  const { t } = useTranslation();
  return (
    <Modal title={title || t('common.viewJson')} size={size} onClose={onClose}
      footer={<button type="button" className="button-secondary" onClick={onClose}>{t('common.close')}</button>}>
      <JsonBlock value={data} copyLabel={copyLabel} />
    </Modal>
  );
}

export default JsonViewer;
