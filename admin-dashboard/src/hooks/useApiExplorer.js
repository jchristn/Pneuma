import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  flattenOpenApiSpec, groupByTag, getParameterDefault, getRequestBodyTemplate,
  substitutePathParams, headersToObject
} from '../utils/openApi';

const HISTORY_KEY = 'pneuma_api_explorer_history';
const MAX_HISTORY = 12;

function loadHistory() {
  try { return JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]'); } catch { return []; }
}
function persistHistory(list) {
  try { localStorage.setItem(HISTORY_KEY, JSON.stringify(list.slice(0, MAX_HISTORY))); } catch { /* ignore */ }
}

export function useApiExplorer(apiClient) {
  const [spec, setSpec] = useState(null);
  const [specError, setSpecError] = useState(null);
  const [specLoading, setSpecLoading] = useState(true);
  const [operationId, setOperationId] = useState(null);
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headers, setHeaders] = useState({});
  const [body, setBody] = useState('');
  const [response, setResponse] = useState(null);
  const [running, setRunning] = useState(false);
  const [history, setHistory] = useState(loadHistory);

  useEffect(() => {
    let cancelled = false;
    setSpecLoading(true);
    apiClient.getOpenApiSpec()
      .then((s) => { if (!cancelled) { setSpec(s); setSpecError(null); } })
      .catch((err) => { if (!cancelled) setSpecError(err?.message || 'Failed to load spec'); })
      .finally(() => { if (!cancelled) setSpecLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient]);

  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);
  const groups = useMemo(() => groupByTag(operations), [operations]);
  const operation = useMemo(() => operations.find((o) => o.id === operationId) || null, [operations, operationId]);

  // Initialize params/body when the selected operation changes.
  const selectOperation = useCallback((id) => {
    setOperationId(id);
    const op = operations.find((o) => o.id === id);
    setResponse(null);
    if (!op) return;
    const pp = {};
    const qp = {};
    (op.parameters || []).forEach((p) => {
      if (p.in === 'path') pp[p.name] = getParameterDefault(p);
      else if (p.in === 'query') qp[p.name] = getParameterDefault(p);
    });
    setPathParams(pp);
    setQueryParams(qp);
    setHeaders({});
    setBody(getRequestBodyTemplate(op.requestBody, spec));
  }, [operations, spec]);

  const resolvedPath = useMemo(() => operation ? substitutePathParams(operation.path, pathParams) : '', [operation, pathParams]);
  const resolvedUrl = useMemo(() => {
    if (!operation) return '';
    const q = new URLSearchParams();
    Object.entries(queryParams).forEach(([k, v]) => { if (v !== '' && v !== undefined && v !== null) q.append(k, v); });
    const qs = q.toString();
    return `${apiClient.baseUrl}${resolvedPath}${qs ? `?${qs}` : ''}`;
  }, [operation, queryParams, resolvedPath, apiClient]);

  const execute = useCallback(async () => {
    if (!operation) return;
    setRunning(true);
    const start = performance.now();
    try {
      const raw = await apiClient.executeExplorer({
        method: operation.method,
        path: resolvedPath,
        query: queryParams,
        headers,
        body
      });
      const text = await raw.text();
      const durationMs = performance.now() - start;
      const parsed = { status: raw.status, statusText: raw.statusText, headers: headersToObject(raw.headers), body: text, durationMs, bytes: text.length };
      setResponse(parsed);
      const item = { id: Date.now(), operationId, method: operation.method, path: resolvedPath, status: raw.status, pathParams, queryParams, headers, body, at: new Date().toISOString() };
      setHistory((prev) => { const next = [item, ...prev].slice(0, MAX_HISTORY); persistHistory(next); return next; });
    } catch (err) {
      setResponse({ status: 0, statusText: 'Network error', headers: {}, body: String(err?.message || err), durationMs: performance.now() - start, error: true });
    } finally {
      setRunning(false);
    }
  }, [operation, resolvedPath, queryParams, headers, body, operationId, pathParams, apiClient]);

  const loadFromHistory = useCallback((item) => {
    setOperationId(item.operationId);
    setPathParams(item.pathParams || {});
    setQueryParams(item.queryParams || {});
    setHeaders(item.headers || {});
    setBody(item.body || '');
    setResponse(null);
  }, []);

  const deleteHistory = useCallback((id) => {
    setHistory((prev) => { const next = prev.filter((h) => h.id !== id); persistHistory(next); return next; });
  }, []);

  return {
    spec, specError, specLoading, groups, operation, operationId, selectOperation,
    pathParams, setPathParams, queryParams, setQueryParams, headers, setHeaders,
    body, setBody, resolvedUrl, resolvedPath, execute, response, running,
    history, loadFromHistory, deleteHistory
  };
}

export default useApiExplorer;
