import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import SearchBox from '../components/SearchBox.jsx';
import NodeCard from '../components/NodeCard.jsx';
import Icon from '../components/Icon.jsx';

export default function SearchView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const query = searchParams.get('q') || '';

  const [input, setInput] = useState(query);
  const [results, setResults] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    setInput(query);
  }, [query]);

  const runSearch = useCallback(
    async (term) => {
      if (!term) {
        setResults(null);
        setError('');
        return;
      }
      setLoading(true);
      setError('');
      try {
        const data = await apiClient.search(term, 20);
        setResults(Array.isArray(data?.results) ? data.results : []);
      } catch (err) {
        setError(err?.message || t('common.error'));
        setResults(null);
      } finally {
        setLoading(false);
      }
    },
    [apiClient, t]
  );

  useEffect(() => {
    runSearch(query);
  }, [query, runSearch]);

  function handleSubmit(term) {
    if (!term) return;
    setSearchParams(term ? { q: term } : {});
  }

  const hasQuery = Boolean(query);
  const resultCount = results?.length ?? 0;

  return (
    <div className={`view search-view${hasQuery ? ' has-query' : ''}`}>
      <section className={`search-hero${hasQuery ? ' compact' : ''}`}>
        {!hasQuery ? (
          <>
            <h1 className="hero-title">{t('search.heroTitle')}</h1>
            <p className="hero-subtitle">{t('search.heroSubtitle')}</p>
          </>
        ) : null}
        <SearchBox
          value={input}
          onChange={setInput}
          onSubmit={handleSubmit}
          placeholder={t('search.placeholder')}
          submitLabel={t('search.submit')}
          busy={loading}
          autoFocus={!hasQuery}
          size={hasQuery ? 'medium' : 'large'}
        />
      </section>

      {loading ? (
        <div className="state-block">
          <div className="spinner" />
          <p>{t('search.searching')}</p>
        </div>
      ) : null}

      {!loading && error ? (
        <div className="state-block state-error">
          <Icon name="alert" size={28} />
          <p>{error}</p>
          <button type="button" className="button button-secondary" onClick={() => runSearch(query)}>
            {t('common.retry')}
          </button>
        </div>
      ) : null}

      {!loading && !error && !hasQuery ? (
        <div className="state-block">
          <Icon name="search" size={28} />
          <h2>{t('search.emptyTitle')}</h2>
          <p>{t('search.emptyBody')}</p>
        </div>
      ) : null}

      {!loading && !error && hasQuery && resultCount === 0 ? (
        <div className="state-block">
          <Icon name="search" size={28} />
          <h2>{t('search.noResultsTitle')}</h2>
          <p>{t('search.noResultsBody', { query })}</p>
        </div>
      ) : null}

      {!loading && !error && hasQuery && resultCount > 0 ? (
        <section className="results">
          <div className="results-header">
            <h2 className="results-title">{t('search.resultsFor', { query })}</h2>
            <span className="results-count">{t('search.resultCount', { count: resultCount })}</span>
          </div>
          <div className="card-grid">
            {results.map((item, index) => (
              <NodeCard
                key={item?.node?.id || index}
                node={item.node}
                snippet={item.snippet}
                score={item.score}
              />
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}
