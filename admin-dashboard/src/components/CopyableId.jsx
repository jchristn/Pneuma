import CopyButton from './CopyButton';

/**
 * An identifier cell: the full id (never truncated) plus a copy-to-clipboard button.
 * The `truncateLen` prop is accepted for backward compatibility but ignored — ids are
 * always shown in full so they can be read and copied accurately.
 */
function CopyableId({ value }) {
  if (value === null || value === undefined || value === '') {
    return <span className="cell-id">—</span>;
  }
  return (
    <span className="copyable-id">
      <code className="cell-id" title={String(value)}>{String(value)}</code>
      <CopyButton value={value} label={null} />
    </span>
  );
}

export default CopyableId;
