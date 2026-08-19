import { useTranslation } from 'react-i18next';

/**
 * Map a domain status string to a pill variant + translated label.
 * Handles link statuses (Submitted/Processing/Ingested/Failed) and job
 * statuses (Queued/Running/Completed/Failed/Pending).
 */
const VARIANT_MAP = {
  submitted: 'info',
  queued: 'info',
  pending: 'neutral',
  processing: 'warning',
  running: 'warning',
  ingested: 'success',
  completed: 'success',
  succeeded: 'success',
  success: 'success',
  failed: 'danger',
  error: 'danger',
  cancelled: 'neutral',
  canceled: 'neutral'
};

const LABEL_KEYS = {
  submitted: 'status.submitted',
  processing: 'status.processing',
  ingested: 'status.ingested',
  failed: 'status.failed',
  queued: 'status.queued',
  completed: 'status.completed',
  running: 'status.running',
  pending: 'status.pending'
};

function StatusPill({ status }) {
  const { t } = useTranslation();
  const raw = (status || '').toString();
  const key = raw.toLowerCase();
  const variant = VARIANT_MAP[key] || 'neutral';
  const label = LABEL_KEYS[key] ? t(LABEL_KEYS[key]) : raw || t('status.unknown');

  return <span className={`status-pill pill-${variant}`}>{label}</span>;
}

export default StatusPill;
