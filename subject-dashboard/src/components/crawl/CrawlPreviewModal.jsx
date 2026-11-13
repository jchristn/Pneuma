import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import CrawlPill from './CrawlPill';
import ConnectivityResultView from './ConnectivityResultView';
import DataGrid from '../grid/DataGrid';
import SummaryCards from '../grid/SummaryCards';
import { formatBytes } from '../../utils/format';
import { PREVIEW_CARD_ROWS, sameFilter, actionTone } from './crawlGrid';

// Runs a connectivity test (mode "test") or a preview (mode "preview") for a stored plan and shows the result.
// Neither changes anything.
function CrawlPreviewModal({ plan, mode, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [facets, setFacets] = useState({});

  const run = useCallback(async () => {
    setLoading(true);
    setError('');
    setResult(null);
    try {
      setResult(mode === 'test' ? await apiClient.testCrawlPlan(plan.id) : await apiClient.previewCrawlPlan(plan.id));
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, plan.id, mode, t]);

  useEffect(() => { run(); }, [run]);

  const columns = useMemo(() => [
    { key: 'key', label: t('crawl.key'), value: (i) => i.key, mono: true, fuzzy: true, width: '44%' },
    { key: 'action', label: t('crawl.action'), value: (i) => i.action, facet: true, width: '11%',
      render: (i) => <CrawlPill label={i.action} tone={actionTone(i.action)} /> },
    { key: 'contentType', label: t('crawl.contentType'), value: (i) => i.contentType || '', facet: true, width: '14%' },
    { key: 'size', label: t('crawl.size'), value: (i) => Number(i.sizeBytes) || 0, width: '9%', align: 'right',
      render: (i) => (i.sizeBytes ? formatBytes(i.sizeBytes) : '-') },
    { key: 'detail', label: t('crawl.detail'), value: (i) => i.detail || '', width: '22%' }
  ], [t]);

  const cardRows = result && mode === 'preview' ? PREVIEW_CARD_ROWS.map((row) => ({
    key: row.key,
    label: t(`crawl.cardRow.${row.key}`, row.key === 'scan' ? 'Found' : 'Would change'),
    cards: row.cards.map((c) => ({
      key: c.count,
      value: result[c.count] ?? 0,
      label: t(`crawl.preview.${c.count}`),
      danger: c.danger,
      active: c.filter ? sameFilter(facets, c.filter) : false,
      onClick: () => setFacets(c.filter && !sameFilter(facets, c.filter) ? c.filter : {})
    }))
  })) : [];

  const footer = (
    <>
      <button type="button" className="btn btn-secondary" onClick={run} disabled={loading}>{t('common.retry', 'Retry')}</button>
      <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
    </>
  );

  return (
    <Modal isOpen title={mode === 'test' ? t('crawl.testTitle', { name: plan.name }) : t('crawl.previewTitle', { name: plan.name })} size={mode === 'preview' ? 'data' : 'large'} onClose={onClose}>
      {loading && <p className="confirm-text">{mode === 'test' ? t('crawl.testing') : t('crawl.previewing')}</p>}
      {error && <div className="form-error">{error}</div>}
      {mode === 'test' && result && <ConnectivityResultView result={result} />}
      {mode === 'preview' && result && (
        <>
          <p className="field-hint">{t('crawl.previewHint')}</p>
          <SummaryCards rows={cardRows} />
          <p className="field-hint">{t('crawl.bytes')}: {formatBytes(result.bytesEnumerated || 0)}</p>
          {result.deletionsHeld && <div className="form-error">{t('crawl.previewDeletionsHeld')}</div>}
          <DataGrid rows={Array.isArray(result.items) ? result.items : []} columns={columns} rowKey={(i) => `${i.action}-${i.key}`}
            facets={facets} onFacetsChange={setFacets} exportName={`crawl-preview-${plan.id}`} emptyText={t('crawl.noObjects')}
            note={result.truncated ? t('crawl.truncated', { count: result.maxItems }) : null} />
        </>
      )}
      <div className="form-actions">{footer}</div>
    </Modal>
  );
}

export default CrawlPreviewModal;
