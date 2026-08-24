import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneLight, oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { WAIT_MESSAGES } from '../components/chatWaitMessages';
import Modal from '../components/Modal';
import ScopeFilter, { buildScopeFilter } from '../components/ScopeFilter';
import ThreadSwitcher from '../components/ThreadSwitcher';

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
 * Replace the whole prior message history with a single compacted-summary turn, keeping only the latest
 * user question and its answer. Mirrors the server-side compaction so the next turn is sent within budget.
 */
function collapseHistory(prev, summary) {
  if (!Array.isArray(prev) || prev.length < 2) return prev;
  const lastAssistant = prev[prev.length - 1];
  const lastUser = prev[prev.length - 2];
  return [
    { role: 'assistant', content: summary, compactedNote: true },
    lastUser,
    lastAssistant,
  ];
}

/** Collapsible model-reasoning section, shown only when the subject enables thinking. Collapsed by default. */
/** Thumbs up/down for one answer. Either rating opens a modal to collect the "why", then submits once. */
function FeedbackBar({ turnId }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [pending, setPending] = useState(null); // 'Up' | 'Down' while the modal is open
  const [comment, setComment] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const textareaRef = useRef(null);
  useEffect(() => {
    if (pending && !sent) {
      const timer = setTimeout(() => textareaRef.current?.focus(), 60);
      return () => clearTimeout(timer);
    }
    return undefined;
  }, [pending, sent]);
  if (!turnId) return null;

  const open = (r) => { setComment(''); setPending(r); };
  const close = () => { if (!busy) setPending(null); };
  const submit = async () => {
    setBusy(true);
    try { await apiClient.submitFeedback(turnId, pending, comment.trim() || null); setSent(true); }
    catch { /* ignore */ }
    finally { setBusy(false); }
  };

  const verb = pending === 'Up' ? t('ask.feedbackLiked', 'liked') : t('ask.feedbackDisliked', 'disliked');
  return (
    <div className="chat-feedback">
      {sent && !pending ? (
        <span className="chat-feedback-done">{t('ask.feedbackThanks', 'Thanks for your feedback.')}</span>
      ) : (
        <>
          <button type="button" className={`fb-btn ${pending === 'Up' ? 'active' : ''}`} disabled={busy || sent} onClick={() => open('Up')} title={t('ask.thumbsUp', 'Helpful')} aria-label={t('ask.thumbsUp', 'Helpful')}>👍</button>
          <button type="button" className={`fb-btn ${pending === 'Down' ? 'active' : ''}`} disabled={busy || sent} onClick={() => open('Down')} title={t('ask.thumbsDown', 'Not helpful')} aria-label={t('ask.thumbsDown', 'Not helpful')}>👎</button>
        </>
      )}
      <Modal isOpen={!!pending} onClose={close} title={t('ask.feedbackModalTitle', 'Share more feedback')} size="small">
        {sent ? (
          <>
            <p style={{ margin: 0 }}>{t('ask.feedbackThankYou', 'Thank you for your feedback!')}</p>
            <div className="form-actions">
              <button type="button" className="btn btn-primary" onClick={close}>{t('common.close', 'Close')}</button>
            </div>
          </>
        ) : (
          <>
            <p style={{ marginBottom: '0.75rem' }}>{t('ask.feedbackPrompt', { verb, defaultValue: 'Tell me more about why you {{verb}} this response.' })}</p>
            <textarea
              ref={textareaRef}
              style={{ width: '100%', resize: 'vertical' }}
              rows={4}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder={t('ask.feedbackComment', 'Add a comment (optional)')}
            />
            <div className="form-actions">
              <button type="button" className="btn btn-secondary" onClick={close} disabled={busy}>{t('common.cancel', 'Cancel')}</button>
              <button type="button" className="btn btn-primary" onClick={submit} disabled={busy}>{busy ? t('common.loading', 'Sending…') : t('common.send', 'Send')}</button>
            </div>
          </>
        )}
      </Modal>
    </div>
  );
}

function Thinking({ thinking, enabled }) {
  const { t } = useTranslation();
  if (!enabled || !thinking) return null;
  return (
    <details className="chat-thinking">
      <summary className="chat-thinking-label">{t('ask.thinking', 'Thinking')}</summary>
      <div className="chat-thinking-content">{thinking}</div>
    </details>
  );
}

/** Clickable citations to the ingested source links the answer drew from. */
function Citations({ citations }) {
  const { t } = useTranslation();
  if (!citations || citations.length === 0) return null;
  return (
    <details className="chat-citations">
      <summary className="chat-citations-label">{t('ask.sources', 'Sources')} ({citations.length})</summary>
      <ol className="chat-citations-list">
        {citations.map((c, i) => (
          <li key={c.linkId || i}>
            <a href={c.url} target="_blank" rel="noopener noreferrer" className="chat-citation" title={c.url}>
              {c.title || c.url}
            </a>
            {typeof c.score === 'number' && c.score > 0 ? (
              <span className="chat-citation-score" title={t('ask.relevance', 'Relevance of this source to the answer')}>{Math.round(c.score * 100)}%</span>
            ) : null}
          </li>
        ))}
      </ol>
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
  if (stats.thinkingEnabled && stats.thinkingMs) rows.push([t('ask.statThinking', 'Thinking time'), `${(stats.thinkingMs / 1000).toFixed(1)} s`]);
  if (stats.contextSize) rows.push([t('ask.statContext', 'Context window'), `${stats.contextSize.toLocaleString()} tokens`]);
  if (stats.contextSize && total) {
    const usedPct = Math.min(100, Math.round((total / stats.contextSize) * 100));
    rows.push([t('ask.statContextUsed', 'Context used'), `${total.toLocaleString()} tokens (${usedPct}%)`]);
  }
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
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [waitMessage, setWaitMessage] = useState('');
  // Optional retrieval scope: narrow answers to content ingested with these labels/tags.
  const [scopeLabels, setScopeLabels] = useState([]);
  const [scopeTags, setScopeTags] = useState([]);
  // Conversation thread switcher: the open thread and a token that forces the switcher to reload its list.
  const [activeThreadId, setActiveThreadId] = useState(null);
  const [activeThreadTitle, setActiveThreadTitle] = useState('');
  const [threadReload, setThreadReload] = useState(0);

  const abortRef = useRef(null);
  const threadIdRef = useRef(null);
  const textareaRef = useRef(null);
  const endRef = useRef(null);
  const recentQuips = useRef([]);

  // Pick a wait-state quip that hasn't been shown recently, so the rotation feels varied.
  const pickQuip = useCallback(() => {
    const available = WAIT_MESSAGES.filter((m) => !recentQuips.current.includes(m));
    const pool = available.length > 0 ? available : WAIT_MESSAGES;
    const picked = pool[Math.floor(Math.random() * pool.length)];
    recentQuips.current.push(picked);
    if (recentQuips.current.length > 20) recentQuips.current.shift();
    return picked;
  }, []);

  // While a request is in flight, show a rotating quip until the first token arrives.
  useEffect(() => {
    if (!streaming) { setWaitMessage(''); return undefined; }
    setWaitMessage(pickQuip());
    const id = setInterval(() => setWaitMessage(pickQuip()), 5000);
    return () => clearInterval(id);
  }, [streaming, pickQuip]);

  // Load the tenant's subjects for the scope selector; auto-select when there is exactly one.
  useEffect(() => {
    let cancelled = false;
    apiClient.getSubjects({ maxResults: 1000 })
      .then((resp) => {
        if (cancelled) return;
        const items = asArray(resp, 'subjects');
        setSubjects(items);
        if (items.length === 1) setSubjectId(items[0].id);
      })
      .catch(() => { if (!cancelled) setSubjects([]); });
    return () => { cancelled = true; };
  }, [apiClient]);

  // Proactively warm the selected subject's answering model so the first question isn't slow to first token.
  useEffect(() => {
    if (subjectId) apiClient.warmup(subjectId).catch(() => {});
  }, [apiClient, subjectId]);

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
    if (!term || streaming || !subjectId) return;

    // Slash commands are handled client-side and never sent to the model.
    if (term.startsWith('/')) {
      const cmd = term.slice(1).split(/\s+/)[0].toLowerCase();
      setInput('');
      if (cmd === 'clear' || cmd === 'new') { threadIdRef.current = null; setActiveThreadId(null); setActiveThreadTitle(''); setMessages([]); setError(null); return; }
      let info;
      if (cmd === 'help' || cmd === '?') {
        info = '**Commands**\n\n| Command | Description |\n|---|---|\n| `/help` or `/?` | Show this list |\n| `/clear` or `/new` | Start a new conversation |\n| `/context` | Show current context usage |\n| `/compact` | Compaction is automatic (informational) |';
      } else if (cmd === 'context') {
        const last = [...messages].reverse().find((m) => m.role === 'assistant' && m.stats);
        const s = last && last.stats;
        if (!s) {
          info = 'No context usage yet — ask a question first.';
        } else {
          const used = s.totalTokens || 0;
          const pct = s.contextSize ? Math.round((used / s.contextSize) * 100) : null;
          const turns = messages.filter((m) => m.role === 'user').length;
          info = '**Context usage**\n\n| Metric | Value |\n|---|---|\n'
            + `| Model | ${s.model || 'unknown'} |\n`
            + `| Context window | ${s.contextSize ? `${s.contextSize.toLocaleString()} tokens` : 'unknown'} |\n`
            + `| Context used | ${used.toLocaleString()} tokens${pct != null ? ` (${pct}%)` : ''} |\n`
            + `| Prompt tokens (last turn) | ${(s.promptTokens || 0).toLocaleString()} |\n`
            + `| Completion tokens (last turn) | ${(s.completionTokens || 0).toLocaleString()} |\n`
            + `| Turns so far | ${turns} |`;
        }
      } else if (cmd === 'compact') {
        info = 'Conversation compaction happens automatically as the context window fills.';
      } else {
        info = `Unknown command "/${cmd}". Try /help.`;
      }
      setMessages((prev) => ([...prev, { role: 'assistant', content: info, streaming: false, tools: [], stats: null, system: true }]));
      return;
    }

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
        subjectId,
        threadId: threadIdRef.current,
        metadataFilter: buildScopeFilter(scopeLabels, scopeTags),
        onEvent: (evt) => {
          if (evt.type === 'delta') {
            patchLast((m) => { m.content += evt.text || ''; m.compacting = false; });
          } else if (evt.type === 'compacting') {
            patchLast((m) => { m.compacting = true; });
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
              m.compacting = false;
              m.citations = Array.isArray(evt.citations) ? evt.citations : [];
              m.turnId = evt.turnId || null;
              if (evt.threadId) {
                threadIdRef.current = evt.threadId;
                setActiveThreadId(evt.threadId);
                if (evt.threadTitle) setActiveThreadTitle(evt.threadTitle);
                setThreadReload((n) => n + 1);
              }
              m.thinking = evt.thinking || '';
              m.thinkingEnabled = !!evt.thinkingEnabled;
              m.stats = {
                model: evt.model || null,
                promptTokens: evt.promptTokens || 0,
                completionTokens: evt.completionTokens || 0,
                totalTokens: evt.totalTokens || 0,
                timeToFirstTokenMs: evt.timeToFirstTokenMs || 0,
                generationMs: evt.generationMs || 0,
                tokensPerSecond: evt.tokensPerSecond || 0,
                contextSize: evt.contextSize || 0,
                thinkingMs: evt.thinkingMs || 0,
                thinkingEnabled: !!evt.thinkingEnabled,
              };
            });
            if (evt.compacted && evt.compactedSummary) {
              setMessages((prev) => collapseHistory(prev, evt.compactedSummary));
            }
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
  }, [apiClient, input, messages, patchLast, streaming, subjectId, t, scopeLabels, scopeTags]);

  const onScopeChange = useCallback(({ labels, tags }) => { setScopeLabels(labels); setScopeTags(tags); }, []);

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
    threadIdRef.current = null;
    setActiveThreadId(null);
    setMessages([]);
    setError(null);
    setInput('');
  }, [streaming]);

  // Rehydrate a past conversation: load its turns and map them into the chat message shape.
  const loadThread = useCallback(async (threadId) => {
    if (streaming || !threadId) return;
    try {
      const data = await apiClient.getThread(threadId);
      const turns = (data && data.turns) || [];
      const msgs = [];
      for (const turn of turns) {
        if (turn.question) msgs.push({ role: 'user', content: turn.question });
        let citations = [];
        if (turn.citationsJson) { try { citations = JSON.parse(turn.citationsJson) || []; } catch { citations = []; } }
        const completion = turn.completionTokens || 0;
        const genMs = turn.generationMs || 0;
        msgs.push({
          role: 'assistant',
          content: turn.answer || '',
          streaming: false,
          tools: [],
          citations,
          turnId: turn.id,
          thinking: turn.thinking || '',
          thinkingEnabled: !!turn.thinking,
          stats: {
            model: turn.model || null,
            promptTokens: turn.promptTokens || 0,
            completionTokens: completion,
            totalTokens: turn.totalTokens || 0,
            timeToFirstTokenMs: turn.timeToFirstTokenMs || 0,
            generationMs: genMs,
            tokensPerSecond: genMs > 0 && completion > 0 ? completion / (genMs / 1000) : 0,
            contextSize: turn.contextSize || 0,
            thinkingMs: turn.thinkingMs || 0,
            thinkingEnabled: !!turn.thinking,
          },
        });
      }
      threadIdRef.current = threadId;
      setActiveThreadId(threadId);
      setActiveThreadTitle((data && data.thread && data.thread.title) || '');
      if (turns[0] && turns[0].subjectId) setSubjectId(turns[0].subjectId);
      setMessages(msgs);
      setError(null);
      setInput('');
    } catch (err) {
      setError(err?.message || t('ask.error', 'The assistant failed to respond.'));
    }
  }, [apiClient, streaming, t]);

  return (
    <div className="view chat-view">
      <div className="chat-view-head">
        <div>
          <h1 className="page-title">{t('nav.ask', 'Ask')}</h1>
          <p className="page-subtitle">{activeThreadTitle || t('ask.subtitle', 'Chat with the corpus. The assistant can search and traverse the knowledge graph to answer.')}</p>
        </div>
        <div className="chat-head-actions">
          <label className="chat-subject-picker">
            <span className="chat-subject-label">{t('ask.subject', 'Subject')}</span>
            <select
              className="chat-subject-select"
              value={subjectId}
              onChange={(e) => setSubjectId(e.target.value)}
              disabled={streaming || subjects.length === 0}
              aria-label={t('ask.subject', 'Subject')}
            >
              <option value="">{subjects.length === 0 ? t('ask.noSubjects', 'No subjects') : t('ask.selectSubject', 'Select a subject')}</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>
              ))}
            </select>
          </label>
          <ThreadSwitcher apiClient={apiClient} subjectId={subjectId || null} activeThreadId={activeThreadId} onSelect={loadThread} onNew={handleNewChat} reloadToken={threadReload} disabled={streaming} />
          {messages.length > 0 ? (
            <button type="button" className="btn btn-secondary" onClick={handleNewChat} disabled={streaming}>
              {t('ask.newChat', 'New chat')}
            </button>
          ) : null}
        </div>
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
                  {message.role !== 'assistant' ? (
                    <p className="chat-user-text">{message.content}</p>
                  ) : message.compactedNote ? (
                    <details className="chat-compacted-note">
                      <summary>{t('ask.compacted', 'Conversation compacted to fit the model’s context')}</summary>
                      <div className="chat-markdown">
                        <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw, rehypeSanitize]} components={MARKDOWN_COMPONENTS}>{message.content}</ReactMarkdown>
                      </div>
                    </details>
                  ) : (
                    <>
                      <ToolTrace tools={message.tools} />
                      <Thinking thinking={message.thinking} enabled={message.thinkingEnabled} />
                      {message.compacting ? (
                        <div className="chat-compacting">{t('ask.compacting', 'Compacting the conversation to fit the model’s context…')}</div>
                      ) : null}
                      {message.content ? (
                        <div className="chat-markdown">
                          <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw, rehypeSanitize]} components={MARKDOWN_COMPONENTS}>
                            {message.content}
                          </ReactMarkdown>
                        </div>
                      ) : (
                        message.streaming && !message.compacting ? (
                          <div className="chat-wait">
                            <div className="chat-typing"><span /><span /><span /></div>
                            {waitMessage ? <span className="chat-wait-text">{waitMessage}</span> : null}
                          </div>
                        ) : null
                      )}
                      {message.streaming && message.content ? <span className="chat-cursor">▍</span> : null}
                      <Citations citations={message.citations} />
                      <StatsInfo stats={message.stats} />
                      {!message.streaming ? <FeedbackBar turnId={message.turnId} /> : null}
                    </>
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
            placeholder={subjectId ? t('ask.placeholder', 'Message Pneuma…') : t('ask.selectSubjectFirst', 'Select a subject to begin…')}
            aria-label={t('ask.placeholder', 'Message Pneuma…')}
            disabled={!subjectId}
            autoFocus
          />
          <button type="button" className="chat-send" onClick={handleSend} disabled={streaming || !input.trim() || !subjectId} aria-label={t('ask.submit', 'Send')}>➤</button>
          <ScopeFilter compact labels={scopeLabels} tags={scopeTags} onChange={onScopeChange} disabled={streaming} />
          <button type="button" className="chat-send chat-stop" onClick={handleStop} disabled={!streaming} aria-label={t('ask.stop', 'Stop')}>■</button>
        </div>
        <p className="chat-disclaimer">{t('ask.disclaimer', 'AI can make mistakes. Please verify all information.')}</p>
      </div>
    </div>
  );
}

export default AskView;
