// Helpers for interpreting an OpenAPI 3 document in the API Explorer.

const METHODS = ['get', 'post', 'put', 'delete', 'patch', 'head', 'options'];

function resolveRef(ref, spec) {
  if (!ref || !ref.startsWith('#/')) return null;
  const parts = ref.slice(2).split('/');
  let node = spec;
  for (const p of parts) {
    if (!node) return null;
    node = node[p];
  }
  return node || null;
}

function resolveSchema(schema, spec, depth = 0) {
  if (!schema || depth > 8) return schema || {};
  if (schema.$ref) return resolveSchema(resolveRef(schema.$ref, spec), spec, depth + 1);
  if (schema.allOf) {
    return schema.allOf.reduce((acc, s) => {
      const resolved = resolveSchema(s, spec, depth + 1);
      return {
        ...acc,
        ...resolved,
        properties: { ...(acc.properties || {}), ...(resolved.properties || {}) }
      };
    }, { type: 'object', properties: {} });
  }
  return schema;
}

/**
 * Flatten an OpenAPI spec into a flat list of operations.
 */
export function flattenOpenApiSpec(spec) {
  if (!spec || !spec.paths) return [];
  const ops = [];
  Object.entries(spec.paths).forEach(([path, pathItem]) => {
    if (!pathItem) return;
    const commonParams = pathItem.parameters || [];
    METHODS.forEach((method) => {
      const op = pathItem[method];
      if (!op) return;
      const tag = (op.tags && op.tags[0]) || 'Default';
      const parameters = [...commonParams, ...(op.parameters || [])].map((p) =>
        p.$ref ? resolveRef(p.$ref, spec) : p
      ).filter(Boolean);
      ops.push({
        id: op.operationId || `${method}_${path}`,
        tag,
        method: method.toUpperCase(),
        path,
        summary: op.summary || op.description || '',
        parameters,
        requestBody: op.requestBody || null,
        responses: op.responses || {}
      });
    });
  });
  return ops;
}

export function groupByTag(operations) {
  const groups = {};
  operations.forEach((op) => {
    if (!groups[op.tag]) groups[op.tag] = [];
    groups[op.tag].push(op);
  });
  return Object.keys(groups)
    .sort((a, b) => a.localeCompare(b))
    .map((tag) => ({ tag, operations: groups[tag] }));
}

export function getParameterDefault(parameter) {
  const schema = parameter.schema || {};
  if (parameter.example !== undefined) return String(parameter.example);
  if (schema.example !== undefined) return String(schema.example);
  if (schema.default !== undefined) return String(schema.default);
  if (Array.isArray(schema.enum) && schema.enum.length) return String(schema.enum[0]);
  return '';
}

function exampleForSchema(schema, spec, depth = 0) {
  const resolved = resolveSchema(schema, spec, depth);
  if (!resolved || depth > 6) return null;
  if (resolved.example !== undefined) return resolved.example;
  if (resolved.default !== undefined) return resolved.default;
  if (Array.isArray(resolved.enum) && resolved.enum.length) return resolved.enum[0];

  switch (resolved.type) {
    case 'object': {
      const obj = {};
      const props = resolved.properties || {};
      Object.entries(props).forEach(([key, propSchema]) => {
        obj[key] = exampleForSchema(propSchema, spec, depth + 1);
      });
      return obj;
    }
    case 'array':
      return [exampleForSchema(resolved.items || {}, spec, depth + 1)].filter((v) => v !== null);
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    case 'string':
      if (resolved.format === 'date-time') return new Date().toISOString();
      return '';
    default:
      if (resolved.properties) {
        const obj = {};
        Object.entries(resolved.properties).forEach(([key, propSchema]) => {
          obj[key] = exampleForSchema(propSchema, spec, depth + 1);
        });
        return obj;
      }
      return '';
  }
}

export function getRequestBodyTemplate(requestBody, spec) {
  if (!requestBody) return '';
  const body = requestBody.$ref ? resolveRef(requestBody.$ref, spec) : requestBody;
  const content = body?.content || {};
  const json = content['application/json'] || content['application/*+json'];
  if (!json || !json.schema) return '';
  const example = exampleForSchema(json.schema, spec);
  if (example === null || example === undefined) return '';
  return JSON.stringify(example, null, 2);
}

export function substitutePathParams(path, pathParams = {}) {
  return path.replace(/\{([^}]+)\}/g, (_, key) => {
    const v = pathParams[key];
    return v !== undefined && v !== '' ? encodeURIComponent(v) : `{${key}}`;
  });
}

export function isDestructive(method, path) {
  return method === 'DELETE' || /\/bulk/i.test(path || '');
}

export function headersToObject(headers) {
  const obj = {};
  headers.forEach((value, key) => { obj[key] = value; });
  return obj;
}

export function buildCodeSnippets({ method, url, headers, body }) {
  const headerLines = Object.entries(headers || {})
    .map(([k, v]) => `  -H '${k}: ${v}'`).join(' \\\n');
  const curl = [
    `curl -X ${method} '${url}'`,
    headerLines,
    body ? `  -d '${body.replace(/'/g, "'\\''")}'` : ''
  ].filter(Boolean).join(' \\\n');

  const fetchSnippet = `fetch('${url}', {\n  method: '${method}',\n  headers: ${JSON.stringify(headers || {}, null, 2)}${body ? `,\n  body: ${JSON.stringify(body)}` : ''}\n});`;

  return { curl, fetch: fetchSnippet };
}
