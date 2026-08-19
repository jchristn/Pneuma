import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';

const PAGE_SIZE = 20;

function SearchView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [query, setQuery] = useState('');
  const [result, setResult] = useState(null);
  const [skip, setSkip] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [searched, setSearched] = useState(false);

  useEffect(() => {
    let cancelled = false;
    apiClient.list('subjects', { maxResults: 1000 })
      .then((resp) => { if (!cancelled) setSubjects(normalizeList(resp).items); })
      .catch(() => { if (!cancelled) setSubjects([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  const runSearch = useCallback(async (nextSkip) => {
    if (!subjectId || !query.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.searchSubjectDocuments(subjectId, query.trim(), { maxResults: PAGE_SIZE, skip: nextSkip });
      setResult(resp);
      setSkip(nextSkip);
      setSearched(true);
    } catch (err) {
      setError(err?.message || 'Search failed');
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId, query]);

  const onSubmit = (e) => { e.preventDefault(); runSearch(0); };

  const objects = result?.objects || result?.Objects || [];
  const total = result?.totalRecords ?? result?.TotalRecords ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);
  const canPrev = skip > 0;
  const canNext = skip + PAGE_SIZE < total;

  return (
    <div>
      <PageHeader title={t('search.title')} subtitle={t('search.subtitle')} />

      <form className="filter-bar" onSubmit={onSubmit}>
        <div className="field" style={{ minWidth: '195px' }}>
          <label htmlFor="search-subject">{t('search.subject')}</label>
          <select id="search-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} required>
            <option value="">{t('search.selectSubject')}</option>
            {subjects.map((c) => (
              <option key={c.id} value={c.id}>{c.displayName || c.name || c.id}</option>
            ))}
          </select>
        </div>
        <div className="field" style={{ flex: 1, minWidth: '240px' }}>
          <label htmlFor="search-query">{t('search.query')}</label>
          <input id="search-query" type="text" value={query} placeholder={t('search.queryPlaceholder')}
            onChange={(e) => setQuery(e.target.value)} />
        </div>
        <div className="field" style={{ alignSelf: 'flex-end' }}>
          <button type="submit" className="button-primary" disabled={!subjectId || !query.trim() || loading}>
            {loading ? t('common.loading') : t('common.search')}
          </button>
        </div>
      </form>

      {error && <ErrorBanner message={error} onDismiss={() => setError(null)} />}

      {searched && !loading && objects.length === 0 && !error && (
        <div className="ilog-empty">{t('search.noResults')}</div>
      )}

      {objects.length > 0 && (
        <>
          <div className="table-frame">
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th style={{ width: '90px' }}>{t('search.score')}</th>
                    <th>{t('search.document')}</th>
                    <th>{t('search.source')}</th>
                  </tr>
                </thead>
                <tbody>
                  {objects.map((r, i) => {
                    const url = r.linkUrl || r.LinkUrl;
                    const title = r.linkTitle || r.LinkTitle;
                    const score = r.score ?? r.Score ?? 0;
                    const snippet = r.snippet || r.Snippet;
                    return (
                      <tr key={r.documentId || r.DocumentId || i}>
                        <td><strong>{Number(score).toFixed(3)}</strong></td>
                        <td className="wrap">{snippet || '—'}</td>
                        <td className="wrap">
                          {url ? (
                            <a href={url} target="_blank" rel="noopener noreferrer">{title || url}</a>
                          ) : '—'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
          <div className="table-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '0.75rem' }}>
            <span className="pagination-info">{t('table.showing', { from, to, total })}</span>
            <div className="pagination-controls">
              <button type="button" onClick={() => runSearch(Math.max(0, skip - PAGE_SIZE))} disabled={!canPrev || loading}>{t('table.prev')}</button>
              <button type="button" onClick={() => runSearch(skip + PAGE_SIZE)} disabled={!canNext || loading}>{t('table.next')}</button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export default SearchView;
