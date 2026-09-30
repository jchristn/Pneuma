import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { normalizeList } from '../../utils/api';
import Modal from '../Modal';
import DataTable from '../DataTable';
import StatusPill from '../StatusPill';
import { violationTone, operationTone, ontologyError, OPERATION_KINDS } from './ontologyUtils';
import { formatDateTime } from '../../i18n/formatters';

const VIOLATION_FILTERS = ['', 'Quarantined', 'Recorded', 'Released', 'Dismissed'];

// What a violation was about, in one line.
function describeElement(v) {
  if (v.elementKind === 'Edge') return `${v.fromNodeName || v.fromNodeType || '?'} —${v.edgeType || '?'}→ ${v.toNodeName || v.toNodeType || '?'}`;
  return `${v.nodeName || '?'} (${v.nodeType || '?'})`;
}

// A subject's ontology violations; quarantined elements can be released into the graph or dismissed.
export function ViolationsPanel({ subjectId, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [status, setStatus] = useState('Quarantined');
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(normalizeList(await apiClient.listOntologyViolations(subjectId, status || null)).items);
      setError('');
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId, status, t]);

  useEffect(() => { load(); }, [load]);

  const resolve = async (v, release) => {
    try {
      if (release) await apiClient.releaseOntologyViolation(v.id); else await apiClient.dismissOntologyViolation(v.id);
      await load();
      onChanged?.();
    } catch (err) {
      setError(ontologyError(err, t('ontology.actionError')));
    }
  };

  const columns = [
    { key: 'createdUtc', label: t('ontology.when'), render: (v) => formatDateTime(v.createdUtc) },
    { key: 'status', label: t('ontology.statusLabel'), render: (v) => <StatusPill label={t(`ontology.violationStatus.${v.status}`)} tone={violationTone(v.status)} /> },
    { key: 'ruleType', label: t('ontology.rule.type'), render: (v) => (v.ruleType ? t(`ontology.ruleType.${v.ruleType}`) : t('ontology.undeclared')) },
    { key: 'element', label: t('ontology.element'), sortable: false, cellClass: 'wrap', render: describeElement },
    { key: 'message', label: t('ontology.message'), sortable: false, cellClass: 'wrap' },
    { key: '_actions', label: t('common.actions'), sortable: false, render: (v) => (v.status === 'Quarantined' ? (
      <span className="onto-counts">
        <button type="button" className="button-secondary button-small" title={t('ontology.releaseTip')} onClick={() => resolve(v, true)}>{t('ontology.release')}</button>
        <button type="button" className="button-secondary button-small" title={t('ontology.dismissTip')} onClick={() => resolve(v, false)}>{t('ontology.dismiss')}</button>
      </span>
    ) : '—') }
  ];

  return (
    <div>
      <p className="field-hint">{t('ontology.violationsHint')}</p>
      <div className="onto-toolbar">
        <label htmlFor="onto-v-status">{t('ontology.statusLabel')}</label>
        <select id="onto-v-status" value={status} onChange={(e) => setStatus(e.target.value)}>
          {VIOLATION_FILTERS.map((s) => <option key={s} value={s}>{s ? t(`ontology.violationStatus.${s}`) : t('ontology.all')}</option>)}
        </select>
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} emptyMessage={t('ontology.noViolations')} />
    </div>
  );
}

// One operation and its per-node items.
function OperationModal({ operationId, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState('');
  useEffect(() => {
    apiClient.getOntologyOperation(operationId).then(setDetail).catch((err) => setError(ontologyError(err, t('ontology.loadError'))));
  }, [apiClient, operationId, t]);
  const op = detail?.operation;
  const columns = [
    { key: 'ordinal', label: '#' },
    { key: 'changed', label: t('ontology.changed'), render: (i) => <StatusPill label={i.changed ? t('ontology.yes') : t('ontology.no')} tone={i.changed ? 'warning' : 'neutral'} /> },
    { key: 'excerpt', label: t('ontology.excerpt'), sortable: false, cellClass: 'wrap', render: (i) => i.excerpt || '—' },
    { key: 'detail', label: t('ontology.detail'), sortable: false, cellClass: 'wrap', render: (i) => i.detail || '—' }
  ];
  return (
    <Modal title={op ? t(`ontology.operationKind.${op.kind}`) : t('ontology.operation')} size="xl" onClose={onClose}
      footer={<button type="button" className="button-primary" onClick={onClose}>{t('common.close')}</button>}>
      {error && <div className="error-message onto-error">{error}</div>}
      {op && (
        <dl className="kv-grid">
          <dt>{t('ontology.statusLabel')}</dt><dd><StatusPill label={op.status} tone={operationTone(op.status)} /></dd>
          <dt>{t('ontology.progress')}</dt><dd>{t('ontology.progressValue', { processed: op.processed, total: op.total, changed: op.changed })}</dd>
          {op.kind === 'Retag' && (<><dt>{t('ontology.linksChanged')}</dt><dd>{t('ontology.addedRemoved', { added: op.added, removed: op.removed })}</dd></>)}
          {op.kind === 'DriftCheck' && (<><dt>{t('ontology.driftRate')}</dt><dd>{`${Math.round((op.driftRate || 0) * 100)}%`}</dd></>)}
          {op.error && (<><dt>{t('ontology.error')}</dt><dd>{op.error}</dd></>)}
        </dl>
      )}
      {detail && <DataTable columns={columns} data={detail.items || []} emptyMessage={t('ontology.noItems')} />}
    </Modal>
  );
}

// Background ontology operations on a subject: validate the graph, re-tag taxonomy links, or check drift.
export function OperationsPanel({ subjectId }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [kind, setKind] = useState('Validate');
  const [sampleSize, setSampleSize] = useState(10);
  const [openId, setOpenId] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(normalizeList(await apiClient.listOntologyOperations(subjectId)).items);
      setError('');
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId, t]);

  useEffect(() => { load(); }, [load]);
  // Refresh while an operation is queued or running.
  useEffect(() => {
    if (!rows.some((r) => r.status === 'Queued' || r.status === 'Running')) return undefined;
    const timer = setTimeout(load, 3000);
    return () => clearTimeout(timer);
  }, [rows, load]);

  const start = async () => {
    setError('');
    try {
      await apiClient.startOntologyOperation(subjectId, kind, Number(sampleSize) || 10);
      await load();
    } catch (err) {
      setError(ontologyError(err, t('ontology.actionError')));
    }
  };

  const columns = [
    { key: 'createdUtc', label: t('ontology.when'), render: (o) => formatDateTime(o.createdUtc) },
    { key: 'kind', label: t('ontology.operationKindLabel'), render: (o) => t(`ontology.operationKind.${o.kind}`) },
    { key: 'status', label: t('ontology.statusLabel'), render: (o) => <StatusPill label={o.status} tone={operationTone(o.status)} /> },
    { key: 'progress', label: t('ontology.progress'), sortable: false, render: (o) => `${o.processed}/${o.total}` },
    { key: 'changed', label: t('ontology.changed') },
    { key: 'driftRate', label: t('ontology.driftRate'), render: (o) => (o.kind === 'DriftCheck' && o.status === 'Succeeded' ? `${Math.round((o.driftRate || 0) * 100)}%` : '—') }
  ];

  return (
    <div>
      <p className="field-hint">{t(`ontology.operationKindTip.${kind}`)}</p>
      <div className="onto-toolbar">
        <select value={kind} onChange={(e) => setKind(e.target.value)} aria-label={t('ontology.operationKindLabel')}>
          {OPERATION_KINDS.map((k) => <option key={k} value={k}>{t(`ontology.operationKind.${k}`)}</option>)}
        </select>
        {kind === 'DriftCheck' && (
          <input type="number" min={1} max={25} value={sampleSize} onChange={(e) => setSampleSize(e.target.value)}
            aria-label={t('ontology.sampleSize')} title={t('ontology.sampleSizeTip')} style={{ width: '6rem' }} />
        )}
        <button type="button" className="button-primary" onClick={start}>{t('ontology.startOperation')}</button>
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={(o) => setOpenId(o.id)} emptyMessage={t('ontology.noOperations')} />
      {openId && <OperationModal operationId={openId} onClose={() => { setOpenId(null); load(); }} />}
    </div>
  );
}
