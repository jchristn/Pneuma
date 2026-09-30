// Shared constants and helpers for the ontology views (tenant ontologies, versions, and a subject's use of one).

export const RULE_TYPES = ['EdgeEndpoints', 'MaxOutgoing', 'RequiredField', 'NamePattern', 'MinConfidence'];
export const RULE_ACTIONS = ['Warn', 'Drop', 'Quarantine', 'Reverse'];
export const UNDECLARED_ACTIONS = ['Allow', 'Warn', 'Drop', 'Quarantine'];
export const NODE_FIELDS = ['Content', 'Rights', 'Authority', 'CanonicalName'];
export const OPERATION_KINDS = ['Validate', 'Retag', 'DriftCheck'];
export const GRAPH_FORMATS = ['json', 'jsonld', 'turtle', 'graphml'];

// Which rule fields each rule type uses (the editor shows only these).
export const RULE_FIELDS = {
  EdgeEndpoints: ['edgeType', 'fromNodeType', 'toNodeType'],
  MaxOutgoing: ['nodeType', 'edgeType', 'maxCount'],
  RequiredField: ['nodeType', 'field'],
  NamePattern: ['nodeType', 'pattern'],
  MinConfidence: ['nodeType', 'edgeType', 'minConfidence']
};

export function versionTone(status) {
  switch (status) {
    case 'Approved': return 'success';
    case 'Draft': return 'info';
    default: return 'neutral';
  }
}

export function violationTone(status) {
  switch (status) {
    case 'Quarantined': return 'warning';
    case 'Released': return 'success';
    case 'Dismissed': return 'neutral';
    default: return 'info';
  }
}

export function operationTone(status) {
  switch (status) {
    case 'Succeeded': return 'success';
    case 'Failed': return 'danger';
    case 'Running': return 'warning';
    default: return 'info';
  }
}

// A blank rule of the given type; the server assigns the id.
export function newRule(ruleType = 'EdgeEndpoints') {
  return { id: '', ruleType, nodeType: null, edgeType: null, fromNodeType: null, toNodeType: null, field: null, pattern: null, maxCount: 1, minConfidence: 0.5, action: 'Warn', description: null };
}

export function newConcept() {
  return { key: '', prefLabel: '', altLabels: [], broaderKey: null, definition: null, nodeType: 'Topic', caseSensitive: false };
}

// The error text for a failed ontology call, including the approval/validation problems the server lists.
export function ontologyError(err, fallback) {
  const payload = err?.parsed || (typeof err?.body === 'object' ? err.body : null);
  const problems = payload?.problems || payload?.Problems;
  const base = err?.message || fallback;
  return Array.isArray(problems) && problems.length ? `${base}\n• ${problems.join('\n• ')}` : base;
}

// Save text as a file in the browser.
export function downloadText(filename, text, mime = 'text/plain') {
  const blob = new Blob([text], { type: `${mime};charset=utf-8` });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export const FORMAT_FILES = {
  json: { ext: 'json', mime: 'application/json' },
  jsonld: { ext: 'jsonld', mime: 'application/ld+json' },
  turtle: { ext: 'ttl', mime: 'text/turtle' },
  graphml: { ext: 'graphml', mime: 'application/xml' }
};

// Pretty-print JSON exports; leave other formats as the server wrote them.
export function exportText(format, text) {
  if (format !== 'json' && format !== 'jsonld') return text;
  try { return JSON.stringify(JSON.parse(text), null, 2); } catch { return text; }
}
