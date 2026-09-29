import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import LabelTagEditor, { toLabelTagPayload } from '../LabelTagEditor';
import ConnectivityResultView from './ConnectivityResultView';
import { settingsProperty, settingsToForm, formToSettings, linesToList } from './crawlUtils';

// One settings field rendered from the type catalog's schema.
function SchemaField({ field, value, onChange, secretSet, clear, onClear }) {
  const { t } = useTranslation();
  const id = `crawl-field-${field.name}`;
  const label = `${field.label}${field.required ? ' *' : ''}`;
  if (field.kind === 'boolean') {
    return (
      <label className="checkbox-field" title={field.help}>
        <input type="checkbox" checked={!!value} onChange={(e) => onChange(e.target.checked)} />
        <span>{field.label}</span>
      </label>
    );
  }
  let input;
  if (field.kind === 'list') {
    input = <textarea id={id} rows={3} value={value} onChange={(e) => onChange(e.target.value)} placeholder={t('crawl.onePerLine')} />;
  } else if (field.kind === 'choice') {
    input = (
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        {(field.options || []).map((o) => <option key={o} value={o}>{o}</option>)}
      </select>
    );
  } else if (field.kind === 'integer') {
    input = <input id={id} type="number" value={value} min={field.min ?? undefined} max={field.max ?? undefined} onChange={(e) => onChange(e.target.value)} />;
  } else if (field.kind === 'secret') {
    input = (
      <div className="password-field">
        <input id={id} type="password" autoComplete="new-password" value={value} disabled={clear}
          placeholder={secretSet ? t('crawl.secretSetPlaceholder') : t('crawl.secretNotSetPlaceholder')}
          onChange={(e) => onChange(e.target.value)} />
        {secretSet && (
          <label className="checkbox-field" title={t('crawl.clearSecretTip')}>
            <input type="checkbox" checked={clear} onChange={(e) => onClear(e.target.checked)} />
            <span>{t('crawl.clearSecret')}</span>
          </label>
        )}
      </div>
    );
  } else {
    input = <input id={id} type="text" value={value} onChange={(e) => onChange(e.target.value)} />;
  }
  return (
    <div className="field">
      <label htmlFor={id} className="has-tip" title={field.help}>{label}</label>
      {input}
      {field.help && <small className="field-hint">{field.help}</small>}
    </div>
  );
}

// Create or edit a crawl plan. The settings section is built from GET /v1.0/crawl-plan-types, so a new connector
// needs no dashboard change. Secrets show as set or not set and are only sent when typed. "Test connection" runs
// the draft against the source without saving it.
function CrawlPlanFormModal({ plan = null, duplicateOf = null, types = [], subjects = [], fixedSubjectId = null, onClose, onSaved }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const source = plan || duplicateOf;
  const editing = !!plan;
  const [name, setName] = useState(source ? (duplicateOf ? `${source.name} (copy)` : source.name) : '');
  const [subjectId, setSubjectId] = useState(fixedSubjectId || source?.subjectId || subjects[0]?.id || '');
  const [type, setType] = useState(source?.type || types[0]?.type || 'Web');
  const typeInfo = useMemo(() => types.find((x) => x.type === type), [types, type]);
  const fields = typeInfo?.fields || [];
  const [values, setValues] = useState(() => settingsToForm(fields, source ? source[settingsProperty(source.type)] : null));
  const [clearSecrets, setClearSecrets] = useState([]);
  const [enabled, setEnabled] = useState(source ? source.enabled !== false : true);
  const [scheduleType, setScheduleType] = useState(source?.schedule?.type || 'Manual');
  const [intervalMinutes, setIntervalMinutes] = useState(String(source?.schedule?.intervalMinutes ?? 1440));
  const [cronExpression, setCronExpression] = useState(source?.schedule?.cronExpression || '0 3 * * *');
  const [timeZone, setTimeZone] = useState(source?.schedule?.timeZone || 'UTC');
  const [include, setInclude] = useState((source?.filter?.includePatterns || []).join('\n'));
  const [exclude, setExclude] = useState((source?.filter?.excludePatterns || []).join('\n'));
  const [contentTypes, setContentTypes] = useState((source?.filter?.allowedContentTypes || []).join('\n'));
  const [minSize, setMinSize] = useState(String(source?.filter?.minSizeBytes ?? 0));
  const [maxSize, setMaxSize] = useState(String(source?.filter?.maxSizeBytes ?? 0));
  const [maxObjects, setMaxObjects] = useState(String(source?.filter?.maxObjects ?? 0));
  const [processAdditions, setProcessAdditions] = useState(source ? source.processAdditions !== false : true);
  const [processUpdates, setProcessUpdates] = useState(source ? source.processUpdates !== false : true);
  const [processDeletions, setProcessDeletions] = useState(source ? !!source.processDeletions : false);
  const [maxDeletionPercent, setMaxDeletionPercent] = useState(String(Math.round((source?.maxDeletionFraction ?? 0.2) * 100)));
  const [retryFailed, setRetryFailed] = useState(source ? source.retryFailedObjects !== false : true);
  const [retentionDays, setRetentionDays] = useState(String(source?.operationRetentionDays ?? 30));
  const [labels, setLabels] = useState(source?.labels || []);
  const [tags, setTags] = useState(Object.entries(source?.tags || {}).map(([key, value]) => ({ key, value })));
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState(null);
  const [error, setError] = useState('');
  const secretsSet = editing ? (plan.secretsSet || []) : [];

  const changeType = (next) => {
    setType(next);
    const info = types.find((x) => x.type === next);
    setValues(settingsToForm(info?.fields || [], null));
    setClearSecrets([]);
    setTestResult(null);
  };

  const buildBody = () => {
    const { labels: l, tags: tg } = toLabelTagPayload(labels, tags);
    return {
      name: name.trim(),
      type,
      enabled,
      [settingsProperty(type)]: formToSettings(fields, values),
      filter: {
        includePatterns: linesToList(include),
        excludePatterns: linesToList(exclude),
        allowedContentTypes: linesToList(contentTypes),
        minSizeBytes: Number(minSize) || 0,
        maxSizeBytes: Number(maxSize) || 0,
        maxObjects: Number(maxObjects) || 0
      },
      schedule: { type: scheduleType, intervalMinutes: Number(intervalMinutes) || 0, cronExpression: scheduleType === 'Cron' ? cronExpression : null, timeZone: timeZone || 'UTC' },
      processAdditions,
      processUpdates,
      processDeletions,
      maxDeletionFraction: Math.min(100, Math.max(0, Number(maxDeletionPercent) || 0)) / 100,
      retryFailedObjects: retryFailed,
      operationRetentionDays: Number(retentionDays) || 30,
      labels: l,
      tags: tg,
      ...(editing ? { clearSecrets } : {})
    };
  };

  const test = async () => {
    setTesting(true);
    setTestResult(null);
    setError('');
    try {
      setTestResult(await apiClient.testCrawlPlanDraft(buildBody(), editing ? plan.id : (duplicateOf?.id || null)));
    } catch (err) {
      setError(err?.message || t('crawl.testError'));
    } finally {
      setTesting(false);
    }
  };

  const save = async () => {
    if (!name.trim()) { setError(t('crawl.nameRequired')); return; }
    if (!editing && !subjectId) { setError(t('crawl.subjectRequired')); return; }
    setSaving(true);
    setError('');
    try {
      const saved = editing ? await apiClient.updateCrawlPlan(plan.id, buildBody()) : await apiClient.createCrawlPlan(subjectId, buildBody());
      onSaved?.(saved);
      onClose();
    } catch (err) {
      setError(err?.message || t('crawl.saveError'));
      setSaving(false);
    }
  };

  const footer = (
    <>
      <button type="button" className="button-secondary" onClick={test} disabled={testing || saving} title={t('crawl.testConnectionTip')}>
        {testing ? t('crawl.testing') : t('crawl.testConnection')}
      </button>
      <button type="button" className="button-secondary" onClick={onClose} disabled={saving}>{t('common.cancel')}</button>
      <button type="button" className="button-primary" onClick={save} disabled={saving}>{saving ? t('common.loading') : t('common.save')}</button>
    </>
  );

  return (
    <Modal title={editing ? t('crawl.editPlan') : t('crawl.createPlan')} subtitle={editing ? plan.id : null} size="xl" onClose={onClose} footer={footer}>
      <div className="form-grid form-grid-2col">
        <div className="form-section-heading field-full">{t('crawl.sectionBasics')}</div>
        <div className="field">
          <label htmlFor="crawl-name">{t('crawl.name')} *</label>
          <input id="crawl-name" type="text" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        {!fixedSubjectId && (
          <div className="field">
            <label htmlFor="crawl-subject" className="has-tip" title={t('crawl.subjectTip')}>{t('crawl.subject')} *</label>
            <select id="crawl-subject" value={subjectId} disabled={editing} onChange={(e) => setSubjectId(e.target.value)}>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>
        )}
        <div className="field">
          <label htmlFor="crawl-type" className="has-tip" title={typeInfo?.description}>{t('crawl.type')} *</label>
          <select id="crawl-type" value={type} disabled={editing} onChange={(e) => changeType(e.target.value)}>
            {types.map((x) => <option key={x.type} value={x.type}>{x.displayName || x.type}</option>)}
          </select>
          {typeInfo?.description && <small className="field-hint">{typeInfo.description}</small>}
        </div>
        <label className="checkbox-field" title={t('crawl.enabledTip')}>
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          <span>{t('crawl.enabled')}</span>
        </label>

        <div className="form-section-heading field-full">{t('crawl.sectionSource')}</div>
        {types.length === 0 && <div className="field-full empty-message">{t('crawl.noTypes')}</div>}
        {fields.map((f) => (
          <div key={f.name} className={f.kind === 'list' ? 'field-full' : ''}>
            <SchemaField field={f} value={values[f.name] ?? ''} secretSet={secretsSet.includes(f.name)}
              clear={clearSecrets.includes(f.name)}
              onClear={(c) => setClearSecrets((prev) => (c ? [...prev, f.name] : prev.filter((n) => n !== f.name)))}
              onChange={(v) => setValues((prev) => ({ ...prev, [f.name]: v }))} />
          </div>
        ))}

        <div className="form-section-heading field-full">{t('crawl.sectionFilter')}</div>
        <div className="field">
          <label htmlFor="crawl-include" className="has-tip" title={t('crawl.includeTip')}>{t('crawl.include')}</label>
          <textarea id="crawl-include" rows={3} value={include} onChange={(e) => setInclude(e.target.value)} placeholder="https://example.com/docs/*" />
        </div>
        <div className="field">
          <label htmlFor="crawl-exclude" className="has-tip" title={t('crawl.excludeTip')}>{t('crawl.exclude')}</label>
          <textarea id="crawl-exclude" rows={3} value={exclude} onChange={(e) => setExclude(e.target.value)} placeholder="*/archive/*" />
        </div>
        <div className="field">
          <label htmlFor="crawl-types" className="has-tip" title={t('crawl.contentTypesTip')}>{t('crawl.contentTypes')}</label>
          <textarea id="crawl-types" rows={3} value={contentTypes} onChange={(e) => setContentTypes(e.target.value)} placeholder="text/html" />
        </div>
        <div className="field">
          <label htmlFor="crawl-max-objects" className="has-tip" title={t('crawl.maxObjectsTip')}>{t('crawl.maxObjects')}</label>
          <input id="crawl-max-objects" type="number" min={0} value={maxObjects} onChange={(e) => setMaxObjects(e.target.value)} />
          <label htmlFor="crawl-min-size" className="has-tip" title={t('crawl.sizeTip')}>{t('crawl.minSize')}</label>
          <input id="crawl-min-size" type="number" min={0} value={minSize} onChange={(e) => setMinSize(e.target.value)} />
          <label htmlFor="crawl-max-size" className="has-tip" title={t('crawl.sizeTip')}>{t('crawl.maxSize')}</label>
          <input id="crawl-max-size" type="number" min={0} value={maxSize} onChange={(e) => setMaxSize(e.target.value)} />
        </div>

        <div className="form-section-heading field-full">{t('crawl.sectionSchedule')}</div>
        <div className="field">
          <label htmlFor="crawl-schedule" className="has-tip" title={t('crawl.scheduleTip')}>{t('crawl.schedule')}</label>
          <select id="crawl-schedule" value={scheduleType} onChange={(e) => setScheduleType(e.target.value)}>
            <option value="Manual">{t('crawl.scheduleManual')}</option>
            <option value="Interval">{t('crawl.scheduleInterval')}</option>
            <option value="Cron">{t('crawl.scheduleCron')}</option>
          </select>
        </div>
        {scheduleType === 'Interval' && (
          <div className="field">
            <label htmlFor="crawl-interval">{t('crawl.intervalMinutes')}</label>
            <input id="crawl-interval" type="number" min={5} max={525600} value={intervalMinutes} onChange={(e) => setIntervalMinutes(e.target.value)} />
            <small className="field-hint">{t('crawl.intervalHint')}</small>
          </div>
        )}
        {scheduleType === 'Cron' && (
          <>
            <div className="field">
              <label htmlFor="crawl-cron" className="has-tip" title={t('crawl.cronTip')}>{t('crawl.cronExpression')}</label>
              <input id="crawl-cron" type="text" value={cronExpression} onChange={(e) => setCronExpression(e.target.value)} />
              <small className="field-hint">{t('crawl.cronHint')}</small>
            </div>
            <div className="field">
              <label htmlFor="crawl-tz" className="has-tip" title={t('crawl.timeZoneTip')}>{t('crawl.timeZone')}</label>
              <input id="crawl-tz" type="text" value={timeZone} onChange={(e) => setTimeZone(e.target.value)} placeholder="UTC" />
            </div>
          </>
        )}

        <div className="form-section-heading field-full">{t('crawl.sectionChanges')}</div>
        <label className="checkbox-field" title={t('crawl.processAdditionsTip')}>
          <input type="checkbox" checked={processAdditions} onChange={(e) => setProcessAdditions(e.target.checked)} />
          <span>{t('crawl.processAdditions')}</span>
        </label>
        <label className="checkbox-field" title={t('crawl.processUpdatesTip')}>
          <input type="checkbox" checked={processUpdates} onChange={(e) => setProcessUpdates(e.target.checked)} />
          <span>{t('crawl.processUpdates')}</span>
        </label>
        <label className="checkbox-field" title={t('crawl.retryFailedTip')}>
          <input type="checkbox" checked={retryFailed} onChange={(e) => setRetryFailed(e.target.checked)} />
          <span>{t('crawl.retryFailed')}</span>
        </label>
        <label className="checkbox-field" title={t('crawl.processDeletionsTip')}>
          <input type="checkbox" checked={processDeletions} onChange={(e) => setProcessDeletions(e.target.checked)} />
          <span>{t('crawl.processDeletions')}</span>
        </label>
        {processDeletions && (
          <div className="field">
            <label htmlFor="crawl-max-delete" className="has-tip" title={t('crawl.maxDeletionTip')}>{t('crawl.maxDeletion')}</label>
            <input id="crawl-max-delete" type="number" min={0} max={100} value={maxDeletionPercent} onChange={(e) => setMaxDeletionPercent(e.target.value)} />
          </div>
        )}
        <div className="field">
          <label htmlFor="crawl-retention" className="has-tip" title={t('crawl.retentionTip')}>{t('crawl.retentionDays')}</label>
          <input id="crawl-retention" type="number" min={1} max={3650} value={retentionDays} onChange={(e) => setRetentionDays(e.target.value)} />
        </div>

        <div className="form-section-heading field-full">{t('crawl.sectionLabels')}</div>
        <div className="field-full">
          <LabelTagEditor labels={labels} tags={tags} onChange={({ labels: l, tags: tg }) => { setLabels(l); setTags(tg); }} />
          <small className="field-hint">{t('crawl.labelsHint')}</small>
        </div>

        {testResult && (
          <div className="field-full">
            <div className="form-section-heading">{t('crawl.testResults')}</div>
            <ConnectivityResultView result={testResult} />
          </div>
        )}
        {error && <div className="error-message field-full">{error}</div>}
      </div>
    </Modal>
  );
}

export default CrawlPlanFormModal;
