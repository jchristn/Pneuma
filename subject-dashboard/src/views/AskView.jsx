import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneLight, oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { useAuth } from '../context/AuthContext';

/** Track the dashboard's light/dark mode from the documentElement data-theme attribute. */
function useThemeMode() {
  const read = () => (document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light');
  const [mode, setMode] = useState(read);
  useEffect(() => {
    const observer = new MutationObserver(() => setMode(read()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    return () => observer.disconnect();
  }, []);
  return mode;
}

/** A fenced code block with a language label and a copy button; inline code falls back to a bare <code>. */
function CodeBlock({ inline, className, children }) {
  const match = /language-(\w+)/.exec(className || '');
  const theme = useThemeMode();
  const [copied, setCopied] = useState(false);
  const code = String(children).replace(/\n$/, '');

  if (inline || !match) {
    return <code className="chat-inline-code">{children}</code>;
  }

  const handleCopy = () => {
    copyText(code).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  };

  return (
    <div className="chat-code-block">
      <div className="chat-code-header">
        <span className="chat-code-lang">{match[1]}</span>
        <button type="button" className="chat-code-copy" onClick={handleCopy}>
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <SyntaxHighlighter
        style={theme === 'dark' ? oneDark : oneLight}
        language={match[1]}
        PreTag="div"
        customStyle={{ margin: 0, borderRadius: '0 0 8px 8px', fontSize: '0.82rem' }}
      >
        {code}
      </SyntaxHighlighter>
    </div>
  );
}

const MARKDOWN_COMPONENTS = {
  code: CodeBlock,
  a: ({ href, children }) => (
    <a href={href} target="_blank" rel="noopener noreferrer" className="chat-md-link">{children}</a>
  ),
  table: ({ children }) => (
    <div className="chat-table-wrapper"><table className="chat-md-table">{children}</table></div>
  ),
};

/** Copy text to the clipboard, falling back to execCommand on insecure (non-HTTPS) origins. */
function copyText(text) {
  if (navigator.clipboard && window.isSecureContext) {
    return navigator.clipboard.writeText(text);
  }
  return new Promise((resolve) => {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); } catch { /* ignore */ }
    document.body.removeChild(area);
    resolve();
  });
}

/** Format a millisecond duration compactly (e.g. "820 ms", "1.2 s"). */
function formatMs(ms) {
  if (ms == null) return null;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

/** Pretty-print a JSON string; fall back to the raw text when it isn't valid JSON. */
function prettyJson(raw) {
  if (raw == null || raw === '') return '';
  try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return String(raw); }
}

/**
 * Collapsible trace of the tools the assistant called for one answer. Each row expands to reveal the
 * tool's query (arguments), its response (result payload), and how long the call took.
 */
function ToolTrace({ tools }) {
  const { t } = useTranslation();
  if (!tools || tools.length === 0) return null;
  return (
    <details className="chat-tool-trace">
      <summary>{t('ask.toolsUsed', 'Used {{count}} tool call(s)', { count: tools.length })}</summary>
      <ul>
        {tools.map((tool, index) => (
          <li key={`${tool.name}-${index}`} className={tool.running ? 'tool-running' : (tool.ok ? 'tool-ok' : 'tool-fail')}>
            <details className="chat-tool-item">
              <summary>
                <code>{tool.name}</code>
                <span className="tool-state">
                  {tool.running ? t('ask.toolRunning', 'running…') : (tool.ok ? t('ask.toolOk', 'ok') : t('ask.toolFail', 'failed'))}
                </span>
                {tool.durationMs != null ? <span className="tool-runtime">{formatMs(tool.durationMs)}</span> : null}
              </summary>
              <div className="chat-tool-detail">
                <div className="chat-tool-block">
                  <span className="chat-tool-label">{t('ask.toolQuery', 'Query')}</span>
                  <pre className="chat-tool-pre">{prettyJson(tool.arguments) || t('ask.toolNoQuery', '(no arguments)')}</pre>
                </div>
                {tool.running ? null : (
                  <div className="chat-tool-block">
                    <span className="chat-tool-label">{t('ask.toolResponse', 'Response')}</span>
                    <pre className="chat-tool-pre">{prettyJson(tool.result) || t('ask.toolNoResponse', '(no response)')}</pre>
                  </div>
                )}
              </div>
            </details>
          </li>
        ))}
      </ul>
    </details>
  );
}

/**
 * Per-answer telemetry (model, TTFT, total time, token counts, throughput) surfaced underneath the
 * response behind a hover/focus (i) affordance so it stays out of the way until wanted.
 */
function StatsInfo({ stats }) {
  const { t } = useTranslation();
  if (!stats) return null;
  const total = stats.totalTokens || ((stats.promptTokens || 0) + (stats.completionTokens || 0));
  const rows = [];
  if (stats.model) rows.push([t('ask.statModel', 'Model'), stats.model]);
  if (stats.timeToFirstTokenMs) rows.push([t('ask.statTtft', 'Time to first token'), `${stats.timeToFirstTokenMs} ms`]);
  if (stats.generationMs) rows.push([t('ask.statGen', 'Total time'), `${(stats.generationMs / 1000).toFixed(1)} s`]);
  if (stats.promptTokens) rows.push([t('ask.statPrompt', 'Prompt tokens'), String(stats.promptTokens)]);
  if (stats.completionTokens) rows.push([t('ask.statCompletion', 'Completion tokens'), String(stats.completionTokens)]);
  if (total) rows.push([t('ask.statTotal', 'Total tokens'), String(total)]);
  if (stats.tokensPerSecond) rows.push([t('ask.statTps', 'Tokens / second'), stats.tokensPerSecond.toFixed(1)]);
  if (rows.length === 0) return null;
  return (
    <div className="chat-stats-info">
      <button type="button" className="chat-stats-trigger" aria-label={t('ask.statsAria', 'Response statistics')}>
        <span className="chat-stats-i" aria-hidden="true">i</span>
        <span className="chat-stats-label">{t('ask.statsLabel', 'Details')}</span>
      </button>
      <div className="chat-stats-popover" role="tooltip">
        <table>
          <tbody>
            {rows.map(([k, v]) => (
              <tr key={k}><th>{k}</th><td>{v}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/**
 * Subject-scoped agentic chat over the corpus. Consistent with the admin dashboard Ask pane: the
 * assistant can call Pneuma's read tools while answering, answers render as Markdown, tool calls are
 * expandable to their query/response/runtime, and per-answer telemetry sits behind a hover (i) label.
 */
function AskView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();

  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [error, setError] = useState(null);

  const abortRef = useRef(null);
  const textareaRef = useRef(null);
  const endRef = useRef(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }, [input]);

  // Mutate the last (assistant) message in place as stream events arrive.
  const patchLast = useCallback((patch) => {
    setMessages((prev) => {
      if (prev.length === 0) return prev;
      const next = prev.slice();
      const last = { ...next[next.length - 1] };
      patch(last);
      next[next.length - 1] = last;
      return next;
    });
  }, []);

  const handleSend = useCallback(async () => {
    const term = input.trim();
    if (!term || streaming) return;

    const history = messages.map((m) => ({ role: m.role, content: m.content }));
    history.push({ role: 'user', content: term });

    setError(null);
    setInput('');
    setStreaming(true);
    setMessages((prev) => ([
      ...prev,
      { role: 'user', content: term },
      { role: 'assistant', content: '', streaming: true, tools: [], stats: null },
    ]));

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      await apiClient.chatStream(history, 8, {
        signal: controller.signal,
        onEvent: (evt) => {
          if (evt.type === 'delta') {
            patchLast((m) => { m.content += evt.text || ''; });
          } else if (evt.type === 'tool_call') {
            patchLast((m) => { m.tools = [...(m.tools || []), { id: evt.id, name: evt.name, running: true, ok: false, arguments: evt.arguments || '' }]; });
          } else if (evt.type === 'tool_result') {
            patchLast((m) => {
              m.tools = (m.tools || []).map((tool) =>
                tool.id === evt.id && tool.running
                  ? { ...tool, running: false, ok: Boolean(evt.ok), result: evt.result ?? (evt.error ? JSON.stringify({ error: evt.error }) : ''), durationMs: typeof evt.durationMs === 'number' ? evt.durationMs : null }
                  : tool);
            });
          } else if (evt.type === 'complete') {
            patchLast((m) => {
              if (!m.content && evt.answer) m.content = evt.answer;
              m.streaming = false;
              m.stats = {
                model: evt.model || null,
                promptTokens: evt.promptTokens || 0,
                completionTokens: evt.completionTokens || 0,
                totalTokens: evt.totalTokens || 0,
                timeToFirstTokenMs: evt.timeToFirstTokenMs || 0,
                generationMs: evt.generationMs || 0,
                tokensPerSecond: evt.tokensPerSecond || 0,
              };
            });
          } else if (evt.type === 'error') {
            setError(evt.message || t('ask.error', 'The assistant failed to respond.'));
            patchLast((m) => { m.streaming = false; });
          }
        },
      });
    } catch (err) {
      if (err?.name !== 'AbortError') {
        setError(err?.message || t('ask.error', 'The assistant failed to respond.'));
      }
      patchLast((m) => { m.streaming = false; });
    } finally {
      setStreaming(false);
      abortRef.current = null;
    }
  }, [apiClient, input, messages, patchLast, streaming, t]);

  const handleStop = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  const handleKeyDown = (event) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      handleSend();
    }
  };

  const handleNewChat = useCallback(() => {
    if (streaming) return;
    setMessages([]);
    setError(null);
    setInput('');
  }, [streaming]);

  return (
    <div className="view chat-view">
      <div className="chat-view-head">
        <div>
          <h1 className="page-title">{t('nav.ask', 'Ask')}</h1>
          <p className="page-subtitle">{t('ask.subtitle', 'Chat with the corpus. The assistant can search and traverse the knowledge graph to answer.')}</p>
        </div>
        {messages.length > 0 ? (
          <button type="button" className="btn btn-secondary" onClick={handleNewChat} disabled={streaming}>
            {t('ask.newChat', 'New chat')}
          </button>
        ) : null}
      </div>

      <div className="chat-scroll">
        {messages.length === 0 ? (
          <div className="chat-empty">
            <h2>{t('ask.emptyTitle', 'Ask Pneuma anything about the corpus')}</h2>
            <p>{t('ask.emptyHint', 'The assistant grounds its answers in the knowledge graph and cites what it finds.')}</p>
          </div>
        ) : (
          <div className="chat-messages">
            {messages.map((message, index) => (
              <div key={index} className={`chat-row chat-row-${message.role}`}>
                <div className="chat-bubble">
                  {message.role === 'assistant' ? (
                    <>
                      <ToolTrace tools={message.tools} />
                      {message.content ? (
                        <div className="chat-markdown">
                          <ReactMarkdown remarkPlugins={[remarkGfm]} components={MARKDOWN_COMPONENTS}>
                            {message.content}
                          </ReactMarkdown>
                        </div>
                      ) : (
                        message.streaming ? (
                          <div className="chat-typing"><span /><span /><span /></div>
                        ) : null
                      )}
                      {message.streaming && message.content ? <span className="chat-cursor">▍</span> : null}
                      <StatsInfo stats={message.stats} />
                    </>
                  ) : (
                    <p className="chat-user-text">{message.content}</p>
                  )}
                </div>
              </div>
            ))}
            <div ref={endRef} />
          </div>
        )}
      </div>

      {error ? <div className="error-banner" role="alert">{error}</div> : null}

      <div className="chat-composer">
        <div className="chat-input-wrap">
          <textarea
            ref={textareaRef}
            className="chat-input"
            value={input}
            rows={1}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={t('ask.placeholder', 'Message Pneuma…')}
            aria-label={t('ask.placeholder', 'Message Pneuma…')}
            autoFocus
          />
          {streaming ? (
            <button type="button" className="chat-send chat-stop" onClick={handleStop} aria-label={t('ask.stop', 'Stop')}>■</button>
          ) : (
            <button type="button" className="chat-send" onClick={handleSend} disabled={!input.trim()} aria-label={t('ask.submit', 'Send')}>➤</button>
          )}
        </div>
        <p className="chat-disclaimer">{t('ask.disclaimer', 'AI can make mistakes. Please verify all information.')}</p>
      </div>
    </div>
  );
}

export default AskView;
