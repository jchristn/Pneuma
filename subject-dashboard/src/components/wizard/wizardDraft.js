// New subject wizard: the draft lives in the browser (sessionStorage) until the subject is created. The server
// drafts each step from the draft it is sent and stores nothing, so a reload resumes where the user left off.

export const STORAGE_KEY = 'pneuma.subjectWizard.v1';

// Steps in order. Steps before "review" edit the draft; after "review" the subject exists.
export const STEPS = ['describe', 'brief', 'questions', 'ontology', 'prompts', 'review', 'sources', 'coverage'];

export const KINDS = ['Fact', 'Relationship', 'Timeline', 'Comparison', 'Reasoning', 'Overview'];

export const PROMPT_KEYS = ['systemPrompt', 'classifyPrompt', 'rewritePrompt', 'rerankingPrompt'];

// The global prompt each subject addition extends (shown next to the draft).
export const PROMPT_GLOBAL_KEYS = {
  systemPrompt: 'user.answer',
  classifyPrompt: 'ontology.classify',
  rewritePrompt: 'prompt.rewrite',
  rerankingPrompt: 'reranking'
};

export const MIN_QUESTIONS = 5;

export function emptyState() {
  return {
    step: 'describe',
    draft: {
      description: '',
      groundingText: '',
      groundingUrl: '',
      brief: null,
      questions: [],
      ontology: null,
      prompts: null
    },
    settings: { modelRunnerId: '', embeddingModel: '', collection: '', ontologyMode: '' },
    // Question texts the ontology was drafted from, to flag an ontology that no longer matches the questions.
    ontologyQuestionsKey: null,
    sources: null,
    created: null,
    coverage: null
  };
}

export function loadState() {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      const base = emptyState();
      return { ...base, ...parsed, draft: { ...base.draft, ...(parsed.draft || {}) }, settings: { ...base.settings, ...(parsed.settings || {}) } };
    }
  } catch { /* storage unavailable or corrupt: start fresh */ }
  return emptyState();
}

export function saveState(state) {
  try { window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch { /* storage unavailable */ }
}

export function clearState() {
  try { window.sessionStorage.removeItem(STORAGE_KEY); } catch { /* storage unavailable */ }
}

export function hasDraft(state) {
  return !!(state && state.draft && (state.draft.description || '').trim());
}

// Enumeration responses come back as arrays or as { objects } / { items } envelopes.
export function listOf(resp) {
  if (Array.isArray(resp)) return resp;
  return resp?.objects || resp?.items || resp?.data || [];
}

export function questionsKey(questions) {
  return JSON.stringify((questions || []).map((q) => (q.question || '').trim()));
}

// Which questions no type serves, and which types serve no question (question numbers are 1-based).
export function ontologyCoverage(ontology, questions) {
  const count = (questions || []).length;
  const served = new Set();
  const unused = [];
  const all = [...(ontology?.nodeTypes || []), ...(ontology?.edgeTypes || [])];
  all.forEach((type) => {
    const qs = (type.questions || []).filter((n) => n >= 1 && n <= count);
    qs.forEach((n) => served.add(n));
    if (qs.length === 0 && type.name && type.name !== 'Subject') unused.push(type.name);
  });
  const unserved = [];
  for (let i = 1; i <= count; i += 1) if (!served.has(i)) unserved.push(i);
  return { unserved, unused: Array.from(new Set(unused)) };
}

// Parse "1, 3 4" into [1, 3, 4].
export function parseNumbers(text) {
  return Array.from(new Set(String(text || '').split(/[^0-9]+/).filter(Boolean).map((n) => parseInt(n, 10)).filter((n) => n > 0))).sort((a, b) => a - b);
}

export function promptValue(prompts, key) {
  return (prompts && prompts[key]) || '';
}
