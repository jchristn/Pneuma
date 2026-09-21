import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';

const PAGE_SIZE_OPTIONS = [10, 20, 50, 100];
const DEFAULT_PAGE_SIZE = 20;

function SearchView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [query, setQuery] = useState('');
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [result, setResult] = useState(null);
  const [skip, setSkip] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [searched, setSearched] = useState(false);
  const [elapsedMs, setElapsedMs] = useState(null);
  const [runnerNames, setRunnerNames] = useState({}); // embedding model id -> display name
  const [warming, setWarming] = useState(false);
  const [warmModelName, setWarmModelName] = useState('');

  useEffect(() => {
    let cancelled = false;
    apiClient.list('subjects', { maxResults: 1000 })
      .then((resp) => { if (!cancelled) setSubjects(normalizeList(resp).items); })
      .catch(() => { if (!cancelled) setSubjects([]); });
    // Model-runner names so we can name the embedding model the moment a subject is chosen (before the
    // warm-up round-trip returns).
    apiClient.list('model-runners', { maxResults: 1000 })
      .then((resp) => {
        if (cancelled) return;
        const map = {};
        normalizeList(resp).items.forEach((r) => { map[r.id || r.Id] = r.name || r.Name || r.id || r.Id; });
        setRunnerNames(map);
      })
      .catch(() => { if (!cancelled) setRunnerNames({}); });
    return () => { cancelled = true; };
  }, [apiClient]);

  // Selecting a subject warms its embedding model (a trivial embed) so the first real search does not pay the
  // model's cold-load cost. The model provider (e.g. Ollama) unloads idle models, so this front-runs the load
  // while the operator is still typing.
  useEffect(() => {
    if (!subjectId) { setWarming(false); setWarmModelName(''); return undefined; }
    const subject = subjects.find((s) => (s.id || s.Id) === subjectId);
    const modelId = subject?.embeddingModel || subject?.EmbeddingModel;
    if (!modelId) { setWarming(false); setWarmModelName(''); return undefined; }

    let cancelled = false;
    setWarmModelName(runnerNames[modelId] || modelId);
    setWarming(true);
    apiClient.warmSubjectEmbedding(subjectId)
      .then((resp) => { if (!cancelled && (resp?.modelName || resp?.ModelName)) setWarmModelName(resp.modelName || resp.ModelName); })
      .catch(() => { /* warm-up is best-effort; the search still works, just cold once */ })
      .finally(() => { if (!cancelled) setWarming(false); });
    return () => { cancelled = true; };
  }, [apiClient, subjectId, subjects, runnerNames]);

  // size is passed explicitly so a page-size change can re-run with the new value without waiting for
  // the state update to flush through the callback's closure.
  const runSearch = useCallback(async (nextSkip, size = pageSize) => {
    if (!subjectId || !query.trim()) return;
    setLoading(true);
    setError(null);
    const started = performance.now();
    try {
      const resp = await apiClient.searchSubjectDocuments(subjectId, query.trim(), { maxResults: size, skip: nextSkip });
      setElapsedMs(performance.now() - started);
      setResult(resp);
      setSkip(nextSkip);
      setSearched(true);
    } catch (err) {
      setElapsedMs(performance.now() - started);
      setError(err?.message || 'Search failed');
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId, query, pageSize]);

  const onSubmit = (e) => { e.preventDefault(); runSearch(0); };

  // Changing the page size restarts pagination and re-runs immediately when a search is already showing.
  const onPageSizeChange = (e) => {
    const size = Number(e.target.value);
    setPageSize(size);
    if (searched) runSearch(0, size);
  };

  const objects = result?.objects || result?.Objects || [];
  const total = result?.totalRecords ?? result?.TotalRecords ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + pageSize, total);
  const canPrev = skip > 0;
  const canNext = skip + pageSize < total;
  const msLabel = elapsedMs == null ? 0 : Math.round(elapsedMs);

  return (
    <div>
      <PageHeader title={t('search.title')} subtitle={t('search.subtitle')} />

      <form className="filter-bar" onSubmit={onSubmit}>
        <div className="field" style={{ minWidth: '195px' }}>
          <label htmlFor="search-subject" className="has-tip" title="Search is scoped to one subject’s ingested documents. Pick which subject to search within.">{t('search.subject')}</label>
          <select id="search-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} required
            title="Search is scoped to one subject’s ingested documents. Pick which subject to search within.">
            <option value="">{t('search.selectSubject')}</option>
            {subjects.map((c) => (
              <option key={c.id} value={c.id}>{c.displayName || c.name || c.id}</option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: 1, minWidth: '240px' }}>
          <label htmlFor="search-query" className="has-tip" title="Keywords to match against the indexed chunk text (full-text search, not a question). Results link to the matching documents.">{t('search.query')}</label>
          <input id="search-query" type="text" value={query} placeholder={t('search.queryPlaceholder')}
            onChange={(e) => setQuery(e.target.value)}
            title="Keywords to match against the indexed chunk text (full-text search, not a question). Results link to the matching documents." />
        </div>
        <div className="field" style={{ minWidth: '110px' }}>
          <label htmlFor="search-max" className="has-tip" title="How many results to fetch and show per page.">{t('search.maxResults')}</label>
          <select id="search-max" value={pageSize} onChange={onPageSizeChange}
            title="How many results to fetch and show per page.">
            {PAGE_SIZE_OPTIONS.map((n) => (
              <option key={n} value={n}>{n}</option>
            ))}
          </select>
        </div>
        <div className="field" style={{ alignSelf: 'flex-end' }}>
          <button type="submit" className="button-primary" disabled={!subjectId || !query.trim() || loading}
            title="Run the full-text search over the selected subject’s documents.">
            {loading ? t('common.loading') : t('common.search')}
          </button>
        </div>
      </form>

      {warming && (
        <div className="table-loading" role="status">
          <div className="loading-spinner" /> {t('search.initializingModel', { name: warmModelName })}
        </div>
      )}

      {error && <ErrorBanner message={error} onDismiss={() => setError(null)} />}

      {searched && !loading && objects.length === 0 && !error && (
        <div className="ilog-empty">{t('search.noResults')} ({msLabel} ms)</div>
      )}

      {objects.length > 0 && (
        <>
          <div className="table-frame">
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th style={{ width: '80px' }}>{t('search.score')}</th>
                    <th style={{ width: '80px' }}>{t('search.matches')}</th>
                    <th>{t('search.docTitle')}</th>
                    <th>{t('search.url')}</th>
                    <th>{t('search.linkId')}</th>
                    <th>{t('search.passage')}</th>
                  </tr>
                </thead>
                <tbody>
                  {objects.map((r, i) => {
                    const url = r.linkUrl || r.LinkUrl;
                    const title = r.linkTitle || r.LinkTitle;
                    const score = r.score ?? r.Score ?? 0;
                    const matches = r.matchCount ?? r.MatchCount ?? 0;
                    const linkId = r.linkId || r.LinkId;
                    const snippet = r.snippet || r.Snippet;
                    return (
                      <tr key={linkId || r.documentId || r.DocumentId || i}>
                        <td><strong>{Number(score).toFixed(3)}</strong></td>
                        <td>{matches || '—'}</td>
                        <td className="wrap">{title || '—'}</td>
                        <td className="wrap">
                          {url ? (
                            <a href={url} target="_blank" rel="noopener noreferrer">{url}</a>
                          ) : '—'}
                        </td>
                        <td>{linkId ? <CopyableId value={linkId} /> : '—'}</td>
                        <td className="wrap">{snippet || '—'}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
          <div className="table-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '0.75rem' }}>
            <span className="pagination-info">{t('search.showing', { from, to, total, ms: msLabel })}</span>
            <div className="pagination-controls">
              <button type="button" onClick={() => runSearch(Math.max(0, skip - pageSize))} disabled={!canPrev || loading}>{t('table.prev')}</button>
              <button type="button" onClick={() => runSearch(skip + pageSize)} disabled={!canNext || loading}>{t('table.next')}</button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export default SearchView;
