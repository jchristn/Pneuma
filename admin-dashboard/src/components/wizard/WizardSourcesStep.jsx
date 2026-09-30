import { useCallback, useEffect, useState } from 'react';
import CrawlPlanFormModal from '../crawl/CrawlPlanFormModal';
import { listOf } from './wizardDraft';
import { GuidanceBar } from './WizardParts';

const TABS = ['links', 'text', 'crawl'];

// Step 7: the subject exists. The model suggests where its content usually lives; the user adds links, pastes text,
// or sets up a crawl plan, and watches ingestion progress. Ingestion carries on if the wizard is closed.
function WizardSourcesStep({ state, generate, busy, apiClient, t, completionRunners, stepModelFor, setStepModel, defaultModelLabel }) {
  const subject = state.created?.subject;
  const [tab, setTab] = useState('links');
  const [urls, setUrls] = useState('');
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [links, setLinks] = useState([]);
  const [working, setWorking] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [types, setTypes] = useState(null);
  const [crawlOpen, setCrawlOpen] = useState(false);

  const load = useCallback(async () => {
    if (!subject) return;
    try { setLinks(listOf(await apiClient.wizardSubjectLinks(subject.id))); } catch { /* keep the last list */ }
  }, [apiClient, subject]);

  useEffect(() => {
    load();
    const id = window.setInterval(load, 5000);
    return () => window.clearInterval(id);
  }, [load]);

  const run = async (action, success) => {
    setWorking(true);
    setError('');
    setMessage('');
    try {
      await action();
      setMessage(success);
      await load();
    } catch (err) {
      setError(err?.message || t('wizard.sources.failed'));
    } finally {
      setWorking(false);
    }
  };

  const addLinks = () => {
    const list = urls.split('\n').map((u) => u.trim()).filter(Boolean);
    if (list.length === 0) return;
    run(() => apiClient.wizardAddLinks(subject.id, list), t('wizard.sources.linksAdded', { count: list.length })).then(() => setUrls(''));
  };
  const addText = () => {
    if (!text.trim()) return;
    run(() => apiClient.wizardAddText(subject.id, title.trim() || null, text), t('wizard.sources.textAdded')).then(() => { setTitle(''); setText(''); });
  };
  const openCrawl = async () => {
    if (!types) {
      try { setTypes(listOf(await apiClient.listCrawlPlanTypes())); } catch { setTypes([]); }
    }
    setCrawlOpen(true);
  };

  const count = (status) => links.filter((l) => String(l.status) === status).length;
  const suggestions = state.sources || [];

  if (!subject) return null;
  return (
    <section className="sw-step">
      <h3>{t('wizard.sources.title', { name: subject.displayName })}</h3>
      <p className="sw-hint">{t('wizard.sources.intro')}</p>

      {suggestions.length > 0 && (
        <div className="sw-card">
          <h4>{t('wizard.sources.suggestions')}</h4>
          <ul className="sw-plain-list">
            {suggestions.map((s, i) => (
              <li key={i}><span className="sw-tag">{s.kind}</span> <strong>{s.title}</strong>{s.detail ? ` - ${s.detail}` : ''}</li>
            ))}
          </ul>
        </div>
      )}
      <GuidanceBar t={t} busy={busy} models={completionRunners} model={stepModelFor('sources')} onModelChange={(id) => setStepModel('sources', id)} defaultModelLabel={defaultModelLabel} regenerateLabel={t('wizard.sources.suggestAgain')} onRegenerate={(guidance) => generate('sources', { guidance })}
        placeholder={t('wizard.sources.guidancePlaceholder')} />

      <div className="sw-tabs" role="tablist">
        {TABS.map((k) => (
          <button key={k} type="button" role="tab" aria-selected={tab === k} className={`sw-tab${tab === k ? ' active' : ''}`} onClick={() => setTab(k)}>
            {t(`wizard.sources.tab.${k}`)}
          </button>
        ))}
      </div>
      {tab === 'links' && (
        <div className="sw-panel-inset">
          <label className="sw-label" htmlFor="sw-urls">{t('wizard.sources.urls')}</label>
          <textarea id="sw-urls" rows={5} value={urls} placeholder="https://..." onChange={(e) => setUrls(e.target.value)} disabled={working} />
          <div className="sw-row sw-end">
            <button type="button" className="sw-btn sw-btn-primary" onClick={addLinks} disabled={working || !urls.trim()}>{t('wizard.sources.addLinks')}</button>
          </div>
        </div>
      )}
      {tab === 'text' && (
        <div className="sw-panel-inset">
          <label className="sw-label" htmlFor="sw-text-title">{t('wizard.sources.textTitle')}</label>
          <input id="sw-text-title" type="text" value={title} maxLength={300} onChange={(e) => setTitle(e.target.value)} disabled={working} />
          <label className="sw-label" htmlFor="sw-text">{t('wizard.sources.text')}</label>
          <textarea id="sw-text" rows={6} value={text} onChange={(e) => setText(e.target.value)} disabled={working} />
          <div className="sw-row sw-end">
            <button type="button" className="sw-btn sw-btn-primary" onClick={addText} disabled={working || !text.trim()}>{t('wizard.sources.addText')}</button>
          </div>
        </div>
      )}
      {tab === 'crawl' && (
        <div className="sw-panel-inset">
          <p className="sw-hint">{t('wizard.sources.crawlHint')}</p>
          <button type="button" className="sw-btn sw-btn-primary" onClick={openCrawl} disabled={working}>{t('wizard.sources.createPlan')}</button>
        </div>
      )}
      {message && <div className="sw-alert sw-alert-success" role="status">{message}</div>}
      {error && <div className="sw-alert sw-alert-error" role="alert">{error}</div>}

      <h4>{t('wizard.sources.progress')}</h4>
      {links.length === 0 ? (
        <p className="sw-muted">{t('wizard.sources.none')}</p>
      ) : (
        <>
          <p className="sw-muted">{t('wizard.sources.summary', { total: links.length, done: count('Ingested'), running: count('Submitted') + count('Processing'), failed: count('Failed') })}</p>
          <div className="sw-table-wrap">
            <table className="sw-table">
              <tbody>
                {links.slice(0, 50).map((l) => (
                  <tr key={l.id}>
                    <td className="sw-wrap">{l.title || l.url}</td>
                    <td><span className={`sw-status sw-status-${String(l.status).toLowerCase()}`}>{t(`wizard.sources.status.${l.status}`, { defaultValue: String(l.status) })}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
      {crawlOpen && (
        <CrawlPlanFormModal types={types || []} subjects={[subject]} fixedSubjectId={subject.id}
          onClose={() => setCrawlOpen(false)}
          onSaved={() => { setCrawlOpen(false); setMessage(t('wizard.sources.planCreated')); load(); }} />
      )}
    </section>
  );
}

export default WizardSourcesStep;
