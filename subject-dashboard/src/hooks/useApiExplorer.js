import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  flattenOpenApiSpec,
  getParameterDefault,
  getRequestBodyTemplate,
  substitutePathParams,
  buildCodeSnippets
} from '../utils/openApi';

const HISTORY_KEY = 'pneuma_subject_explorer_history';
const MAX_HISTORY = 12;

function loadHistory() {
  try {
    return JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]');
  } catch {
    return [];
  }
}
function persistHistory(history) {
  try {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history.slice(0, MAX_HISTORY)));
  } catch {
    // ignore quota errors
  }
}

function headersToObject(headers) {
  const obj = {};
  headers.forEach((value, key) => {
    obj[key] = value;
  });
  return obj;
}

/**
 * OpenAPI-driven API Explorer state. Loads /openapi.json once, exposes the
 * flattened operation list, per-operation request fields, execution, and a
 * persisted history capped at 12 entries.
 */
export function useApiExplorer(apiClient, serverUrl) {
  const [spec, setSpec] = useState(null);
  const [specError, setSpecError] = useState('');
  const [specLoading, setSpecLoading] = useState(true);

  const [operationId, setOperationId] = useState(null);
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headers, setHeaders] = useState({});
  const [body, setBody] = useState('');

  const [response, setResponse] = useState(null);
  const [executing, setExecuting] = useState(false);
  const [history, setHistory] = useState(loadHistory);

  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);

  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    setSpecLoading(true);
    apiClient
      .getOpenApiSpec()
      .then((result) => {
        if (!cancelled) {
          setSpec(result);
          setSpecError('');
        }
      })
      .catch((err) => {
        if (!cancelled) setSpecError(err.message || 'Failed to load OpenAPI spec');
      })
      .finally(() => {
        if (!cancelled) setSpecLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [apiClient]);

  const operation = useMemo(
    () => operations.find((op) => op.id === operationId) || null,
    [operations, operationId]
  );

  // When the operation changes, seed defaults.
  useEffect(() => {
    if (!operation) return;
    const nextPath = {};
    const nextQuery = {};
    for (const param of operation.parameters) {
      if (param.in === 'path') nextPath[param.name] = getParameterDefault(param);
      else if (param.in === 'query') nextQuery[param.name] = getParameterDefault(param);
    }
    setPathParams(nextPath);
    setQueryParams(nextQuery);
    setHeaders({});
    setBody(getRequestBodyTemplate(operation.requestBody, spec));
    setResponse(null);
  }, [operation, spec]);

  const resolvedPath = useMemo(
    () => (operation ? substitutePathParams(operation.path, pathParams) : ''),
    [operation, pathParams]
  );

  const resolvedUrl = useMemo(() => {
    if (!operation) return '';
    const base = (serverUrl || '').replace(/\/+$/, '');
    const qs = Object.entries(queryParams)
      .filter(([, v]) => v !== undefined && v !== null && v !== '')
      .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
      .join('&');
    return `${base}${resolvedPath}${qs ? `?${qs}` : ''}`;
  }, [operation, serverUrl, resolvedPath, queryParams]);

  const snippets = useMemo(() => {
    if (!operation) return { curl: '', fetch: '' };
    return buildCodeSnippets({
      method: operation.method,
      url: resolvedUrl,
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer <token>', ...headers },
      body: operation.method !== 'GET' && operation.method !== 'HEAD' ? body : ''
    });
  }, [operation, resolvedUrl, headers, body]);

  const isDestructive = useMemo(() => {
    if (!operation) return false;
    return operation.method === 'DELETE' || operation.path.includes('/bulk');
  }, [operation]);

  const execute = useCallback(async () => {
    if (!operation || !apiClient) return;
    setExecuting(true);
    setResponse(null);
    const start = performance.now();
    try {
      const hasBody = operation.method !== 'GET' && operation.method !== 'HEAD' && body;
      const raw = await apiClient.executeExplorer({
        method: operation.method,
        path: resolvedPath,
        query: queryParams,
        headers,
        body: hasBody ? body : undefined
      });
      const durationMs = performance.now() - start;
      const text = await raw.text();
      const parsed = {
        status: raw.status,
        statusText: raw.statusText,
        headers: headersToObject(raw.headers),
        body: text,
        durationMs,
        sizeBytes: new Blob([text]).size
      };
      setResponse(parsed);

      const historyItem = {
        id: Date.now().toString(),
        timestamp: new Date().toISOString(),
        operationId: operation.id,
        method: operation.method,
        path: operation.path,
        pathParams,
        queryParams,
        headers,
        body,
        status: raw.status
      };
      setHistory((prev) => {
        const next = [historyItem, ...prev].slice(0, MAX_HISTORY);
        persistHistory(next);
        return next;
      });
    } catch (err) {
      setResponse({ error: err.message, durationMs: performance.now() - start });
    } finally {
      setExecuting(false);
    }
  }, [operation, apiClient, resolvedPath, queryParams, headers, body, pathParams]);

  const loadFromHistory = useCallback(
    (item) => {
      setOperationId(item.operationId);
      // apply saved fields after the operation-seeding effect runs
      setTimeout(() => {
        setPathParams(item.pathParams || {});
        setQueryParams(item.queryParams || {});
        setHeaders(item.headers || {});
        setBody(item.body || '');
      }, 0);
    },
    []
  );

  const deleteHistoryItem = useCallback((id) => {
    setHistory((prev) => {
      const next = prev.filter((h) => h.id !== id);
      persistHistory(next);
      return next;
    });
  }, []);

  return {
    spec,
    specError,
    specLoading,
    operations,
    operation,
    operationId,
    setOperationId,
    pathParams,
    setPathParams,
    queryParams,
    setQueryParams,
    headers,
    setHeaders,
    body,
    setBody,
    resolvedUrl,
    snippets,
    isDestructive,
    response,
    executing,
    execute,
    history,
    loadFromHistory,
    deleteHistoryItem
  };
}

export default useApiExplorer;
