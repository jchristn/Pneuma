import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';

/**
 * Subject-scoped grounded chat. Asks a natural-language question of the corpus
 * and streams the grounded answer over server-sent events, rendering deltas live.
 */
function AskView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();

  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState(null);
  const [streaming, setStreaming] = useState(false);
  const [error, setError] = useState('');

  async function handleSubmit(event) {
    event.preventDefault();
    const term = question.trim();
    if (!term) return;

    setStreaming(true);
    setError('');
    setAnswer({ answer: '', sources: [], grounded: false });

    let accumulated = { answer: '', sources: [], grounded: false };
    try {
      await apiClient.askStream(term, 10, {
        onEvent: (evt) => {
          if (evt.type === 'metadata') {
            accumulated = { ...accumulated, grounded: Boolean(evt.grounded) };
            setAnswer({ ...accumulated });
          } else if (evt.type === 'delta') {
            accumulated = { ...accumulated, answer: accumulated.answer + (evt.text || '') };
            setAnswer({ ...accumulated });
          } else if (evt.type === 'complete') {
            accumulated = {
              answer: evt.answer ?? accumulated.answer,
              sources: Array.isArray(evt.sources) ? evt.sources : accumulated.sources,
              grounded: Boolean(evt.grounded),
              model: evt.model ?? null,
              generationMs: typeof evt.generationMs === 'number' ? evt.generationMs : null,
            };
            setAnswer({ ...accumulated });
          } else if (evt.type === 'error') {
            setError(evt.message || t('common.error', 'Something went wrong.'));
          }
        },
      });
    } catch (err) {
      setError(err?.message || t('common.error', 'Something went wrong.'));
    } finally {
      setStreaming(false);
    }
  }

  const sources = Array.isArray(answer?.sources) ? answer.sources : [];

  return (
    <div className="view ask-view">
      <h1 className="page-title">{t('nav.ask', 'Ask')}</h1>
      <p className="page-subtitle">{t('ask.subtitle', 'Ask a grounded question of your archive; the answer streams as it is generated.')}</p>

      <form onSubmit={handleSubmit} className="ask-form">
        <input
          type="text"
          className="ask-input"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder={t('ask.placeholder', 'Ask a question…')}
          aria-label={t('ask.placeholder', 'Ask a question…')}
          autoFocus
        />
        <button type="submit" className="button" disabled={streaming}>
          {streaming ? t('ask.streaming', 'Streaming…') : t('ask.submit', 'Ask')}
        </button>
      </form>

      {error ? <div className="error-banner" role="alert">{error}</div> : null}

      {answer ? (
        <section className="answer-section">
          <div className={`answer-card${answer.grounded ? '' : ' answer-ungrounded'}`}>
            <div className="answer-head">
              <h2 className="panel-title">{t('ask.answerTitle', 'Answer')}</h2>
              <span className={`pill ${answer.grounded ? 'pill-grounded' : 'pill-ungrounded'}`}>
                {answer.grounded ? t('ask.groundedYes', 'Grounded') : t('ask.groundedNo', 'Ungrounded')}
              </span>
            </div>
            <p className="answer-text answer-text-prewrap">{answer.answer}</p>
            {answer.model ? (
              <p className="answer-meta">
                {t('ask.answeredBy', 'Answered by {{model}}', { model: answer.model })}
                {typeof answer.generationMs === 'number' ? ` · ${answer.generationMs} ms` : ''}
              </p>
            ) : null}
          </div>

          {sources.length > 0 ? (
            <div className="sources">
              <h2 className="results-title">{t('ask.sourcesTitle', 'Sources')}</h2>
              <ul>
                {sources.map((source, index) => {
                  const node = source?.node || source;
                  return <li key={node?.id || index}>{node?.name || node?.id}</li>;
                })}
              </ul>
            </div>
          ) : null}
        </section>
      ) : null}
    </div>
  );
}

export default AskView;
