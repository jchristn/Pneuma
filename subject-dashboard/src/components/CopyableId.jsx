import CopyButton from './CopyButton';

/**
 * Displays a value (id, URL, endpoint) with a copy affordance. The value never
 * wraps; it truncates and preserves the exact string on copy.
 */
function CopyableId({ value, title = 'Copy' }) {
  if (value === null || value === undefined || value === '') {
    return <span className="copyable-id-value">n/a</span>;
  }
  return (
    <span className="copyable-id">
      <span className="copyable-id-value" title={String(value)}>
        {String(value)}
      </span>
      <CopyButton value={value} title={title} />
    </span>
  );
}

export default CopyableId;
