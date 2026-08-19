/**
 * Helpers for working with Pneuma graph nodes and edges on the client.
 *
 * Nodes have the shape:
 *   { id, nodeType, name, canonicalName, content, labels, tags }
 * Edges have the shape:
 *   { id, edgeType, fromNodeId, toNodeId, tags }
 */

const URL_REGEX = /\bhttps?:\/\/[^\s<>"')\]]+/gi;

/** Best display name for a node. */
export function nodeDisplayName(node) {
  if (!node) return '';
  return node.name || node.canonicalName || node.id || 'Untitled';
}

/** Normalize tags into a plain object regardless of source shape. */
export function tagsObject(tags) {
  if (!tags) return {};
  if (Array.isArray(tags)) {
    // Array of {key,value} or ["a","b"] — collapse into an object where we can.
    const out = {};
    for (const entry of tags) {
      if (entry && typeof entry === 'object' && 'key' in entry) {
        out[entry.key] = entry.value;
      }
    }
    return out;
  }
  if (typeof tags === 'object') return tags;
  return {};
}

/** Normalize labels into a string array. */
export function labelsArray(labels) {
  if (!labels) return [];
  if (Array.isArray(labels)) return labels.filter(Boolean).map(String);
  if (typeof labels === 'string') return [labels];
  return [];
}

/**
 * Extract rights / authority related tags from a node, if present.
 * Returns an array of { key, value } pairs.
 */
export function rightsTags(node) {
  const tags = tagsObject(node?.tags);
  const keysOfInterest = /(right|rights|authority|license|licence|provenance|source|canonical|confidence|attribution|trust|access)/i;
  return Object.entries(tags)
    .filter(([key, value]) => keysOfInterest.test(key) && value !== undefined && value !== null && value !== '')
    .map(([key, value]) => ({ key, value: String(value) }));
}

/**
 * Collect every link found in a node: any URL inside its content text plus a
 * `tags.url` (and common URL-bearing tag keys). De-duplicated, order preserved.
 * @returns {Array<{url:string, label:string}>}
 */
export function extractLinks(node) {
  if (!node) return [];
  const found = new Map();

  const add = (url, label) => {
    if (!url) return;
    const trimmed = String(url).trim().replace(/[.,;]+$/, '');
    if (!/^https?:\/\//i.test(trimmed)) return;
    if (!found.has(trimmed)) found.set(trimmed, label || trimmed);
  };

  const tags = tagsObject(node.tags);
  for (const [key, value] of Object.entries(tags)) {
    if (typeof value === 'string' && /url|link|href|source|uri/i.test(key)) {
      add(value, value);
    }
  }

  if (typeof node.content === 'string') {
    const matches = node.content.match(URL_REGEX) || [];
    for (const match of matches) add(match, match);
  }

  return Array.from(found, ([url, label]) => ({ url, label }));
}

/** Turn an EDGE_TYPE constant into readable words, e.g. HAS_TRACK -> "Has track". */
export function humanizeEdgeType(edgeType) {
  if (!edgeType) return 'Related to';
  const words = String(edgeType)
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .trim()
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * Build a human-readable description for an edge relative to the current node.
 * Example output: "Has track → Rebel Without a Pause".
 *
 * @param {object} edge The edge record.
 * @param {string} currentNodeId The node whose page we are on.
 * @param {Map<string,object>} nodesById Lookup of adjacent nodes by id.
 */
export function describeEdge(edge, currentNodeId, nodesById) {
  const type = humanizeEdgeType(edge?.edgeType);
  const outgoing = edge?.fromNodeId === currentNodeId;
  const otherId = outgoing ? edge?.toNodeId : edge?.fromNodeId;
  const other = otherId ? nodesById.get(otherId) : null;
  const otherName = other ? nodeDisplayName(other) : otherId || 'unknown node';
  const arrow = outgoing ? '→' : '←';
  return { type, arrow, otherId, otherName, outgoing };
}
