import CopyButton from './CopyButton';

/**
 * Read-only JSON/code viewer with a copy button. Accepts an object or string.
 */
function JsonViewer({ value, label }) {
  const text =
    typeof value === 'string' ? value : JSON.stringify(value ?? {}, null, 2);

  return (
    <div className="json-viewer">
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: 6
        }}
      >
        {label && (
          <span className="detail-label" style={{ fontSize: '0.72rem' }}>
            {label}
          </span>
        )}
        <CopyButton value={text} title="Copy" />
      </div>
      <pre>{text || '(empty)'}</pre>
    </div>
  );
}

export default JsonViewer;
