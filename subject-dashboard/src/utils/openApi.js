/**
 * OpenAPI helpers for the API Explorer.
 * Flatten a spec into a list of operations and derive default field values,
 * example request bodies, and code snippets.
 */

const HTTP_METHODS = ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'];

/**
 * Flatten an OpenAPI spec into a flat list of operations.
 * Each operation: { id, tag, method, path, summary, parameters, requestBody, responses }
 */
export function flattenOpenApiSpec(spec) {
  if (!spec || !spec.paths) return [];
  const operations = [];

  for (const [path, pathItem] of Object.entries(spec.paths)) {
    if (!pathItem || typeof pathItem !== 'object') continue;
    const sharedParams = Array.isArray(pathItem.parameters) ? pathItem.parameters : [];

    for (const method of HTTP_METHODS) {
      const op = pathItem[method];
      if (!op) continue;

      const parameters = [
        ...sharedParams,
        ...(Array.isArray(op.parameters) ? op.parameters : [])
      ].map((p) => resolveRef(p, spec));

      const tag = (op.tags && op.tags[0]) || 'Default';
      const id = op.operationId || `${method.toUpperCase()} ${path}`;

      operations.push({
        id,
        tag,
        method: method.toUpperCase(),
        path,
        summary: op.summary || op.description || id,
        parameters,
        requestBody: op.requestBody ? resolveRef(op.requestBody, spec) : null,
        responses: op.responses || {}
      });
    }
  }

  operations.sort((a, b) => {
    if (a.tag !== b.tag) return a.tag.localeCompare(b.tag);
    return a.path.localeCompare(b.path);
  });

  return operations;
}

export function groupOperationsByTag(operations) {
  const groups = new Map();
  for (const op of operations) {
    if (!groups.has(op.tag)) groups.set(op.tag, []);
    groups.get(op.tag).push(op);
  }
  return Array.from(groups.entries()).map(([tag, ops]) => ({ tag, ops }));
}

function resolveRef(node, spec, depth = 0) {
  if (!node || typeof node !== 'object' || depth > 20) return node;
  if (node.$ref && typeof node.$ref === 'string') {
    const target = getByPointer(spec, node.$ref);
    return resolveRef(target, spec, depth + 1);
  }
  return node;
}

function getByPointer(spec, ref) {
  if (!ref.startsWith('#/')) return null;
  const parts = ref.slice(2).split('/');
  let current = spec;
  for (const part of parts) {
    if (current == null) return null;
    current = current[decodeURIComponent(part.replace(/~1/g, '/').replace(/~0/g, '~'))];
  }
  return current;
}

/**
 * Sensible default value for a parameter (example, default, enum[0], type zero).
 */
export function getParameterDefault(parameter) {
  const schema = parameter.schema || {};
  if (parameter.example !== undefined) return String(parameter.example);
  if (schema.example !== undefined) return String(schema.example);
  if (schema.default !== undefined) return String(schema.default);
  if (Array.isArray(schema.enum) && schema.enum.length > 0) return String(schema.enum[0]);
  return '';
}

/**
 * Build an example JSON body from a requestBody definition. Resolves $ref and allOf.
 */
export function getRequestBodyTemplate(requestBody, spec) {
  if (!requestBody) return '';
  const content = requestBody.content || {};
  const json = content['application/json'] || content[Object.keys(content)[0]];
  if (!json) return '';

  if (json.example !== undefined) {
    return JSON.stringify(json.example, null, 2);
  }
  const schema = resolveRef(json.schema, spec);
  const example = buildExampleFromSchema(schema, spec);
  return example === undefined ? '' : JSON.stringify(example, null, 2);
}

function buildExampleFromSchema(schema, spec, depth = 0) {
  if (!schema || depth > 12) return undefined;
  schema = resolveRef(schema, spec, 0);
  if (!schema) return undefined;

  if (schema.example !== undefined) return schema.example;
  if (schema.default !== undefined) return schema.default;
  if (Array.isArray(schema.enum) && schema.enum.length > 0) return schema.enum[0];

  if (Array.isArray(schema.allOf)) {
    let merged = {};
    for (const part of schema.allOf) {
      const built = buildExampleFromSchema(part, spec, depth + 1);
      if (built && typeof built === 'object') merged = { ...merged, ...built };
    }
    return merged;
  }
  if (Array.isArray(schema.oneOf) && schema.oneOf.length) {
    return buildExampleFromSchema(schema.oneOf[0], spec, depth + 1);
  }
  if (Array.isArray(schema.anyOf) && schema.anyOf.length) {
    return buildExampleFromSchema(schema.anyOf[0], spec, depth + 1);
  }

  const type = schema.type || (schema.properties ? 'object' : undefined);

  switch (type) {
    case 'object': {
      const obj = {};
      const props = schema.properties || {};
      for (const [key, propSchema] of Object.entries(props)) {
        obj[key] = buildExampleFromSchema(propSchema, spec, depth + 1);
      }
      return obj;
    }
    case 'array':
      return [buildExampleFromSchema(schema.items, spec, depth + 1)].filter((v) => v !== undefined);
    case 'string':
      return schema.format === 'date-time' ? new Date().toISOString() : '';
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    default:
      return null;
  }
}

export function substitutePathParams(path, pathParams) {
  return path.replace(/\{([^}]+)\}/g, (_, name) => {
    const value = pathParams[name];
    return value !== undefined && value !== '' ? encodeURIComponent(value) : `{${name}}`;
  });
}

/**
 * Build curl and fetch code snippets for a composed request.
 */
export function buildCodeSnippets({ method, url, headers, body }) {
  const headerEntries = Object.entries(headers || {}).filter(([, v]) => v);

  const curlLines = [`curl -X ${method} "${url}"`];
  for (const [k, v] of headerEntries) {
    curlLines.push(`  -H "${k}: ${v}"`);
  }
  if (body) {
    curlLines.push(`  -d '${body.replace(/'/g, "'\\''")}'`);
  }
  const curl = curlLines.join(' \\\n');

  const fetchHeaders = JSON.stringify(Object.fromEntries(headerEntries), null, 2);
  const fetchLines = [
    `await fetch("${url}", {`,
    `  method: "${method}",`,
    `  headers: ${fetchHeaders},`
  ];
  if (body) {
    fetchLines.push(`  body: ${JSON.stringify(body)}`);
  }
  fetchLines.push('});');
  const fetchSnippet = fetchLines.join('\n');

  return { curl, fetch: fetchSnippet };
}
