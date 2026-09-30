import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import { KINDS, listOf } from './wizardDraft';
import './SubjectWizard.css';

// A subject's starter questions: suggestions on the ask page and the questions the coverage check asks.
function StarterQuestionsModal({ subject, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [questions, setQuestions] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    let cancelled = false;
    apiClient.getSubjectQuestions(subject.id)
      .then((resp) => { if (!cancelled) setQuestions(listOf(resp)); })
      .catch((err) => { if (!cancelled) { setQuestions([]); setError(err?.message || ''); } });
    return () => { cancelled = true; };
  }, [apiClient, subject.id]);

  const change = (i, patch) => setQuestions((qs) => qs.map((q, j) => (j === i ? { ...q, ...patch, origin: 'User' } : q)));
  const save = async () => {
    setSaving(true);
    setError('');
    setNotice('');
    try {
      const saved = await apiClient.setSubjectQuestions(subject.id, questions.filter((q) => (q.question || '').trim()));
      setQuestions(listOf(saved));
      setNotice(t('wizard.starter.saved'));
    } catch (err) {
      setError(err?.message || '');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t('wizard.starter.title')}
      subtitle={subject.displayName || subject.name}
      size="lg"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={onClose}>{t('common.close')}</button>
          <button type="button" className="button-primary" onClick={save} disabled={saving || questions === null}>{t('common.save')}</button>
        </>
      )}
    >
      <p className="field-hint">{t('wizard.starter.intro')}</p>
      {error && <div className="form-error">{error}</div>}
      {notice && <div className="field-hint" role="status">{notice}</div>}
      {questions === null ? (
        <div className="table-loading"><div className="loading-spinner" /></div>
      ) : (
        <>
          {questions.length === 0 && <p className="field-hint">{t('wizard.starter.empty')}</p>}
          {questions.map((q, i) => (
            <div key={q.id || i} className="sq-row">
              <input type="text" className="sq-question" value={q.question} maxLength={500} onChange={(e) => change(i, { question: e.target.value })}
                aria-label={t('wizard.questions.question', { n: i + 1 })} />
              <select className="sq-kind" value={q.kind || 'Fact'} onChange={(e) => change(i, { kind: e.target.value })} aria-label={t('wizard.questions.kind')}>
                {KINDS.map((k) => <option key={k} value={k}>{t(`wizard.kind.${k}`)}</option>)}
              </select>
              <button type="button" className="button-secondary sq-remove" onClick={() => setQuestions((qs) => qs.filter((_, j) => j !== i))} aria-label={t('wizard.remove')}>✕</button>
            </div>
          ))}
          <button type="button" className="button-secondary" onClick={() => setQuestions((qs) => [...qs, { question: '', kind: 'Fact', origin: 'User' }])}>
            {t('wizard.questions.add')}
          </button>
        </>
      )}
    </Modal>
  );
}

export default StarterQuestionsModal;
