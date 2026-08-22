import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import Modal from './Modal';
import CopyableId from './CopyableId';
import CopyButton from './CopyButton';
import { formatDateTime } from '../i18n/formatters';

function fmtMs(v) {
  if (v == null) return '—';
  const n = Number(v);
  if (!isFinite(n) || n <= 0) return '—';
  return n < 1000 ? `${Math.round(n)} ms` : `${(n / 1000).toFixed(2)} s`;
}

function fmtNum(v) {
  const n = Number(v);
  return isFinite(n) && n > 0 ? n.toLocaleString() : '—';
}

function fmtTps(completionTokens, generationMs) {
  if (!(completionTokens > 0) || !(generationMs > 0)) return '—';
  return `${(completionTokens / (generationMs / 1000)).toFixed(1)} tok/s`;
}

// Distinct colors for the per-stage timing bars.
const STAGE_COLORS = ['#4dabf7', '#38d9a9', '#a9e34b', '#ffd43b', '#ffa94d', '#ff6b6b', '#da77f2', '#845ef7', '#20c997', '#3bc9db'];

// Humanize a pipeline stage name (snake/kebab → Title Case; surface a `tool:` prefix).
function humanizeStage(name) {
  if (!name) return 'Stage';
  const n = String(name);
  if (n.startsWith('tool:')) return `Tool · ${n.slice(5)}`;
  return n.replace(/[_-]+/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

function Metric({ label, value, hint, accent }) {
  return (
    <div className="hd-metric" style={accent ? { borderLeftColor: accent } : undefined} title={hint}>
      <span className="hd-metric-label">{label}</span>
      <span className="hd-metric-value">{value}</span>
    </div>
  );
}

function TimingBar({ label, durationMs, maxMs, color, hint }) {
  const pct = maxMs > 0 && durationMs > 0 ? Math.max(2, (durationMs / maxMs) * 100) : 0;
  return (
    <div className="hd-timing-row" title={hint}>
      <span className="hd-timing-label">{label}</span>
      <span className="hd-timing-track">
        {pct > 0 && <span className="hd-timing-fill" style={{ width: `${Math.min(pct, 100)}%`, background: color }} />}
      </span>
      <span className="hd-timing-value">{fmtMs(durationMs)}</span>
    </div>
  );
}

function MarkdownPanel({ text, empty }) {
  const value = (text ?? '').toString();
  if (!value.trim()) return <div className="hd-panel-body hd-muted">{empty}</div>;
  return (
    <div className="hd-panel-body chat-markdown">
      <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>{value}</ReactMarkdown>
    </div>
  );
}

/**
 * Rich, full-width detail view for a single chat turn: identifiers, roll-up metrics, a timing
 * visualization, token/context usage bars, markdown-rendered question/answer/thinking with
 * copy-to-clipboard controls, a citations table, and any feedback recorded against the turn.
 */
export default function HistoryDetailModal({ detail, subjectName, onClose }) {
  const { t } = useTranslation();
  const [thinkingOpen, setThinkingOpen] = useState(false);

  const turn = detail?.turn || detail || null;
  const feedback = detail?.feedback || [];

  const citations = useMemo(() => {
    try { return turn?.citationsJson ? JSON.parse(turn.citationsJson) : []; }
    catch { return []; }
  }, [turn]);

  const stages = useMemo(() => {
    try {
      const p = turn?.performanceJson ? JSON.parse(turn.performanceJson) : null;
      return p && Array.isArray(p.stages) ? p.stages : [];
    } catch { return []; }
  }, [turn]);

  const retrievalFilter = useMemo(() => {
    try { return turn?.retrievalFilterJson ? JSON.parse(turn.retrievalFilterJson) : null; }
    catch { return null; }
  }, [turn]);

  const toolCalls = detail?.toolCalls || [];

  if (!turn) return null;

  const total = turn.totalTokens || ((turn.promptTokens || 0) + (turn.completionTokens || 0));
  const contextPct = turn.contextSize > 0 ? Math.min(100, (total / turn.contextSize) * 100) : 0;
  const promptPct = total > 0 ? ((turn.promptTokens || 0) / total) * 100 : 0;
  const ttltMs = (turn.timeToFirstTokenMs || 0) + (turn.generationMs || 0);
  const tpsOverall = ttltMs > 0 && turn.completionTokens > 0 ? `${(turn.completionTokens / (ttltMs / 1000)).toFixed(1)} tok/s` : '—';
  const wallMs = stages.reduce((s, x) => s + (x.durationMs || 0), 0);
  // Shared max for the phase-timing bars so they read comparably against each other.
  const timingMax = Math.max(turn.timeToFirstTokenMs || 0, turn.generationMs || 0, turn.thinkingMs || 0, ttltMs, wallMs);
  // Longest single stage, driving the per-stage bar lengths.
  const stageBarMax = stages.reduce((m, x) => Math.max(m, x.durationMs || 0), 0);

  return (
    <Modal title={t('history.detailTitle', 'Chat turn')} size="wide" onClose={onClose}
      footer={<button type="button" className="button-secondary" onClick={onClose}>{t('common.close', 'Close')}</button>}>
      <div className="hd">
        {/* Identifiers */}
        <div className="hd-ids">
          <div className="hd-id"><span className="hd-id-label">{t('history.id', 'Turn ID')}</span><CopyableId value={turn.id} /></div>
          {turn.subjectId && <div className="hd-id"><span className="hd-id-label">{t('history.subject', 'Subject')}</span><span className="hd-id-val">{subjectName ? subjectName(turn.subjectId) : turn.subjectId}</span><CopyButton value={turn.subjectId} title={t('common.copy', 'Copy')} /></div>}
          {turn.userId && <div className="hd-id"><span className="hd-id-label">{t('history.user', 'User')}</span><CopyableId value={turn.userId} /></div>}
          <div className="hd-id"><span className="hd-id-label">{t('history.model', 'Model')}</span><span className="hd-id-val">{turn.model || '—'}</span></div>
          <div className="hd-id"><span className="hd-id-label">{t('history.created', 'Created')}</span><span className="hd-id-val">{formatDateTime(turn.createdUtc)}</span></div>
        </div>

        {/* Performance timing — the duration KPIs rendered as horizontal bars for at-a-glance comparison. */}
        {timingMax > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.timing', 'Performance timing')}</div>
            <div className="hd-timing">
              <TimingBar label={t('history.ttft', 'Time to first token')} durationMs={turn.timeToFirstTokenMs} maxMs={timingMax} color="#4dabf7" hint="Elapsed time from sending the prompt to the first streamed token." />
              <TimingBar label={t('history.thinkingTime', 'Thinking')} durationMs={turn.thinkingMs} maxMs={timingMax} color="#845ef7" hint="Time the model spent in its reasoning phase before answering." />
              <TimingBar label={t('history.gen', 'Generation')} durationMs={turn.generationMs} maxMs={timingMax} color="#ff6b6b" hint="Time spent streaming the answer, from first token to last." />
              <TimingBar label={t('history.ttlt', 'Time to last token')} durationMs={ttltMs} maxMs={timingMax} color="#f783ac" hint="Prompt sent to last token (time to first token + generation)." />
              {wallMs > 0 && <TimingBar label={t('history.wall', 'Pipeline wall time')} durationMs={wallMs} maxMs={timingMax} color="#a9e34b" hint="Total measured time across all answer-pipeline stages (rewrite, retrieval, tools, generation)." />}
            </div>
          </div>
        )}

        {/* Per-stage timing bars (from the recorded pipeline stages), sized by each stage's duration. */}
        {stageBarMax > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.stageTiming', 'Time per stage')}</div>
            <div className="hd-timing">
              {stages.filter((s) => (s.durationMs || 0) > 0).map((s, i) => (
                <TimingBar key={`sb-${s.name || 'stage'}-${i}`} label={humanizeStage(s.name)} durationMs={s.durationMs} maxMs={stageBarMax}
                  color={STAGE_COLORS[i % STAGE_COLORS.length]} hint={`${s.kind || 'stage'}${s.model ? ` · ${s.model}` : ''}`} />
              ))}
            </div>
          </div>
        )}

        {/* Scalar roll-up metrics (throughput, tokens, context) kept as compact cards. */}
        <div className="hd-metrics">
          <Metric label={t('history.tpsGen', 'Throughput (gen)')} value={fmtTps(turn.completionTokens, turn.generationMs)} accent="#20c997" hint="Completion tokens per second over the generation window." />
          <Metric label={t('history.tpsOverall', 'Throughput (overall)')} value={tpsOverall} accent="#20c997" hint="Completion tokens per second over the whole answer (prompt sent to last token)." />
          <Metric label={t('history.promptTokens', 'Prompt tokens')} value={fmtNum(turn.promptTokens)} hint="Tokens in the assembled prompt (system + context + question)." />
          <Metric label={t('history.completionTokens', 'Completion tokens')} value={fmtNum(turn.completionTokens)} hint="Tokens in the assistant's answer." />
          <Metric label={t('history.totalTokens', 'Total tokens')} value={fmtNum(total)} hint="Prompt tokens plus completion tokens." />
          <Metric label={t('history.context', 'Context window')} value={turn.contextSize > 0 ? `${turn.contextSize.toLocaleString()} tok` : '—'} hint="Model context window this turn ran against." />
        </div>

        {/* Per-stage details */}
        {stages.length > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.stages', 'Stage details')}</div>
            <div className="hd-table-wrap">
              <table className="hd-table">
                <thead>
                  <tr>
                    <th>{t('history.stage', 'Stage')}</th>
                    <th>{t('history.stageKind', 'Kind')}</th>
                    <th>{t('history.stageModel', 'Model')}</th>
                    <th>{t('history.stageDuration', 'Duration')}</th>
                    <th>{t('history.stageTtft', 'First token')}</th>
                    <th>{t('history.stageTokens', 'Tokens (in / out)')}</th>
                  </tr>
                </thead>
                <tbody>
                  {stages.map((s, i) => (
                    <tr key={`${s.name || 'stage'}-${i}`}>
                      <td>{s.name || '—'}</td>
                      <td className="hd-muted">{s.kind || '—'}</td>
                      <td>{s.model || '—'}</td>
                      <td>{fmtMs(s.durationMs)}</td>
                      <td>{s.timeToFirstTokenMs > 0 ? fmtMs(s.timeToFirstTokenMs) : '—'}</td>
                      <td>{(s.promptTokens || s.completionTokens) ? `${fmtNum(s.promptTokens)} / ${fmtNum(s.completionTokens)}` : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Token & context usage */}
        {total > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.tokenUsage', 'Token & context usage')}</div>
            <div className="hd-usage">
              <div className="hd-usage-row">
                <span className="hd-usage-caption">{t('history.tokenSplit', 'Prompt vs completion')} · {fmtNum(turn.promptTokens)} / {fmtNum(turn.completionTokens)}</span>
                <span className="hd-stack">
                  <span className="hd-stack-seg" style={{ width: `${promptPct}%`, background: '#4dabf7' }} title={`${t('history.promptTokens', 'Prompt tokens')}: ${fmtNum(turn.promptTokens)}`} />
                  <span className="hd-stack-seg" style={{ width: `${100 - promptPct}%`, background: '#ff6b6b' }} title={`${t('history.completionTokens', 'Completion tokens')}: ${fmtNum(turn.completionTokens)}`} />
                </span>
              </div>
              {turn.contextSize > 0 && (
                <div className="hd-usage-row">
                  <span className="hd-usage-caption">{t('history.contextFill', 'Context window used')} · {total.toLocaleString()} / {turn.contextSize.toLocaleString()} ({contextPct.toFixed(1)}%)</span>
                  <span className="hd-stack">
                    <span className="hd-stack-seg" style={{ width: `${contextPct}%`, background: '#20c997' }} />
                  </span>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Retrieval filter */}
        {retrievalFilter && ((retrievalFilter.required && retrievalFilter.required.length > 0) || (retrievalFilter.excluded && retrievalFilter.excluded.length > 0)) && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.retrievalFilter', 'Retrieval filter applied')}</div>
            <div className="hd-filter">
              {(retrievalFilter.required || []).map((c, i) => (
                <span key={`r-${i}`} className="hd-filter-chip">{c.key} {c.condition} {c.value ?? ''}</span>
              ))}
              {(retrievalFilter.excluded || []).map((c, i) => (
                <span key={`e-${i}`} className="hd-filter-chip hd-filter-exc">not ({c.key} {c.condition} {c.value ?? ''})</span>
              ))}
            </div>
          </div>
        )}

        {/* Messages */}
        <div className="hd-section">
          <div className="hd-panel">
            <div className="hd-panel-head">
              <span className="hd-panel-title">{t('history.question', 'Question')}</span>
              <CopyButton value={turn.question || ''} title={t('history.copyQuestion', 'Copy question')} />
            </div>
            <MarkdownPanel text={turn.question} empty="—" />
          </div>
          <div className="hd-panel">
            <div className="hd-panel-head">
              <span className="hd-panel-title">{t('history.answer', 'Answer')}</span>
              <CopyButton value={turn.answer || ''} title={t('history.copyAnswer', 'Copy answer')} />
            </div>
            <MarkdownPanel text={turn.answer} empty="—" />
          </div>
          {turn.thinking ? (
            <div className="hd-panel">
              <div className="hd-panel-head hd-panel-toggle" onClick={() => setThinkingOpen((v) => !v)}>
                <span className="hd-panel-title">{thinkingOpen ? '▼' : '▶'} {t('history.thinking', 'Thinking')}</span>
                <CopyButton value={turn.thinking || ''} title={t('history.copyThinking', 'Copy thinking')} />
              </div>
              {thinkingOpen && <div className="hd-panel-body hd-pre">{turn.thinking}</div>}
            </div>
          ) : null}
        </div>

        {/* Citations */}
        {citations.length > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.citations', 'Citations')} <span className="hd-count">{citations.length}</span></div>
            <div className="hd-table-wrap">
              <table className="hd-table">
                <thead>
                  <tr>
                    <th style={{ width: '2.5rem' }}>#</th>
                    <th>{t('history.citationTitle', 'Title')}</th>
                    <th>{t('history.citationUrl', 'URL')}</th>
                    <th style={{ width: '2.5rem' }} />
                  </tr>
                </thead>
                <tbody>
                  {citations.map((c, i) => (
                    <tr key={c.linkId || c.url || i}>
                      <td className="hd-td-num">{i + 1}</td>
                      <td>{c.title || '—'}</td>
                      <td className="hd-td-url">{c.url ? <a href={c.url} target="_blank" rel="noopener noreferrer">{c.url}</a> : '—'}</td>
                      <td>{c.url ? <CopyButton value={c.url} title={t('history.copyUrl', 'Copy URL')} /> : null}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Tool activity */}
        {toolCalls.length > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.toolActivity', 'Tool activity')} <span className="hd-count">{toolCalls.length}</span></div>
            <div className="hd-table-wrap">
              <table className="hd-table">
                <thead>
                  <tr>
                    <th style={{ width: '2.5rem' }}>#</th>
                    <th>{t('history.tool', 'Tool')}</th>
                    <th>{t('common.status', 'Status')}</th>
                    <th>{t('history.stageDuration', 'Runtime')}</th>
                    <th>{t('history.toolDetails', 'Arguments / output')}</th>
                  </tr>
                </thead>
                <tbody>
                  {toolCalls.map((c, i) => (
                    <tr key={c.id || i}>
                      <td className="hd-td-num">{(c.sequence ?? i) + 1}</td>
                      <td>{c.toolName || '—'}</td>
                      <td><span className={c.success ? 'hd-ok' : 'hd-fail'}>{c.success ? t('history.ok', 'ok') : t('history.failed', 'failed')}</span></td>
                      <td>{fmtMs(c.durationMs)}</td>
                      <td>
                        {c.argumentsJson ? <CopyButton value={c.argumentsJson} title={t('history.copyArgs', 'Copy arguments')} /> : null}
                        {c.outputJson ? <CopyButton value={c.outputJson} title={t('history.copyOutput', 'Copy output')} /> : null}
                        {!c.argumentsJson && !c.outputJson ? <span className="hd-muted">—</span> : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Feedback */}
        {feedback.length > 0 && (
          <div className="hd-section">
            <div className="hd-section-title">{t('history.feedback', 'Feedback')} <span className="hd-count">{feedback.length}</span></div>
            <div className="hd-feedback">
              {feedback.map((f, i) => (
                <div key={f.id || i} className="hd-feedback-item">
                  <span className={`hd-rating hd-rating-${(f.rating || 'none').toLowerCase()}`}>
                    {f.rating === 'Up' ? '👍' : f.rating === 'Down' ? '👎' : '💬'}
                  </span>
                  <div className="hd-feedback-body">
                    {f.comment ? <div className="chat-markdown"><ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>{f.comment}</ReactMarkdown></div> : <span className="hd-muted">{t('history.noComment', 'No comment')}</span>}
                    <div className="hd-feedback-meta">{formatDateTime(f.createdUtc)}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
