import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import SearchBox from '../components/SearchBox.jsx';
import NodeCard from '../components/NodeCard.jsx';
import Icon from '../components/Icon.jsx';

export default function AskView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();

  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  async function handleAsk(term) {
    if (!term) return;
    setLoading(true);
    setError('');
    setAnswer(null);

    let accumulated = { answer: '', sources: [], grounded: false };
    let firstEvent = true;

    try {
      await apiClient.askStream(term, 10, {
        onEvent: (event) => {
          // Reveal the answer surface as soon as the first event arrives so deltas render live.
          if (firstEvent) {
            firstEvent = false;
            setLoading(false);
          }
          if (event.type === 'metadata') {
            accumulated = { ...accumulated, grounded: Boolean(event.grounded) };
            setAnswer({ ...accumulated });
          } else if (event.type === 'delta') {
            accumulated = { ...accumulated, answer: accumulated.answer + (event.text || '') };
            setAnswer({ ...accumulated });
          } else if (event.type === 'complete') {
            accumulated = {
              answer: event.answer ?? accumulated.answer,
              sources: Array.isArray(event.sources) ? event.sources : accumulated.sources,
              grounded: Boolean(event.grounded),
              model: event.model ?? null,
              generationMs: typeof event.generationMs === 'number' ? event.generationMs : null,
            };
            setAnswer({ ...accumulated });
          } else if (event.type === 'error') {
            setError(event.message || t('common.error'));
          }
        },
      });
    } catch (err) {
      setError(err?.message || t('common.error'));
    } finally {
      setLoading(false);
    }
  }

  const sources = Array.isArray(answer?.sources) ? answer.sources : [];
  const grounded = Boolean(answer?.grounded);

  return (
    <div className="view ask-view">
      <section className="search-hero">
        <h1 className="hero-title">{t('ask.heroTitle')}</h1>
        <p className="hero-subtitle">{t('ask.heroSubtitle')}</p>
        <SearchBox
          value={question}
          onChange={setQuestion}
          onSubmit={handleAsk}
          placeholder={t('ask.placeholder')}
          submitLabel={t('ask.submit')}
          busy={loading}
          autoFocus
          size="large"
        />
      </section>

      {loading ? (
        <div className="state-block">
          <div className="spinner" />
          <p>{t('ask.asking')}</p>
        </div>
      ) : null}

      {!loading && error ? (
        <div className="state-block state-error">
          <Icon name="alert" size={28} />
          <p>{error}</p>
          <button type="button" className="button button-secondary" onClick={() => handleAsk(question.trim())}>
            {t('common.retry')}
          </button>
        </div>
      ) : null}

      {!loading && !error && !answer ? (
        <div className="state-block">
          <Icon name="ask" size={28} />
          <h2>{t('ask.emptyTitle')}</h2>
          <p>{t('ask.emptyBody')}</p>
        </div>
      ) : null}

      {!loading && !error && answer ? (
        <section className="answer-section">
          <div className={`answer-card${grounded ? '' : ' answer-ungrounded'}`}>
            <div className="answer-head">
              <h2 className="panel-title">{t('ask.answerTitle')}</h2>
              <span className={`pill ${grounded ? 'pill-grounded' : 'pill-ungrounded'}`}>
                {grounded ? t('ask.groundedYes') : t('ask.groundedNo')}
              </span>
            </div>
            <p className="answer-text">{answer.answer}</p>
            {answer.model ? (
              <p className="answer-meta">
                {t('ask.answeredBy', { model: answer.model })}
                {typeof answer.generationMs === 'number' ? ` · ${answer.generationMs} ms` : ''}
              </p>
            ) : null}
          </div>

          <div className="sources">
            <h2 className="results-title">{t('ask.sourcesTitle')}</h2>
            {sources.length > 0 ? (
              <div className="card-grid">
                {sources.map((source, index) => {
                  // Sources may be bare nodes or { node, snippet, score } wrappers.
                  const node = source?.node || source;
                  return <NodeCard key={node?.id || index} node={node} snippet={source?.snippet} score={source?.score} />;
                })}
              </div>
            ) : (
              <p className="muted">{t('ask.noSources')}</p>
            )}
          </div>
        </section>
      ) : null}
    </div>
  );
}
