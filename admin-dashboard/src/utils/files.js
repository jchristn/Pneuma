/**
 * File-upload helpers shared by the Links view, the upload modal, and the link detail modal: display names for
 * links whose content was uploaded or pushed (their URL is an internal pneuma-inline:// URI), a client-side
 * pre-check against the server's upload settings, dropped-folder expansion, and saving a downloaded blob.
 */

const INLINE_PREFIX = 'pneuma-inline://';

/** Whether a link's content was uploaded or pushed through the API (it has no fetchable URL). */
export function isInlineLink(link) {
  if (!link) return false;
  if (link.sourceKind) return String(link.sourceKind) === 'Inline';
  return String(link.url || '').startsWith(INLINE_PREFIX);
}

/** The file name an uploaded link carries, falling back to the name encoded at the end of its inline URI. */
export function linkFileName(link) {
  if (!link) return '';
  if (link.fileName) return link.fileName;
  const url = String(link.url || '');
  if (!url.startsWith(INLINE_PREFIX)) return '';
  const rest = url.slice(INLINE_PREFIX.length);
  const slash = rest.indexOf('/');
  if (slash < 0) return '';
  try { return decodeURIComponent(rest.slice(slash + 1)); } catch { return rest.slice(slash + 1); }
}

/** A short label for where a link's content came from: File, Text, Crawl, or Url. */
export function linkKind(link) {
  if (!link) return 'Url';
  if (linkFileName(link)) return 'File';
  if (isInlineLink(link)) return 'Text';
  if (String(link.sourceKind || '') === 'Crawl' || link.crawlPlanId) return 'Crawl';
  return 'Url';
}

/** The name to show for a link: its file name, then (for pushed text) its title, then its URL. */
export function linkDisplayName(link) {
  if (!link) return '';
  const file = linkFileName(link);
  if (file) return file;
  if (isInlineLink(link)) return link.title || link.externalKey || link.id || link.url;
  return link.url || link.title || link.id || '';
}

/** Human-readable byte size (1 decimal place above 1 KB). */
export function formatFileSize(bytes) {
  const n = Number(bytes);
  if (!Number.isFinite(n) || n < 0) return '-';
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = n / 1024;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024;
    i += 1;
  }
  return `${value.toFixed(1)} ${units[i]}`;
}

/** The lower-case extension of a file name without the dot, or ''. */
export function fileExtension(name) {
  const s = String(name || '');
  const dot = s.lastIndexOf('.');
  if (dot <= 0 || dot === s.length - 1) return '';
  return s.slice(dot + 1).toLowerCase();
}

/**
 * Check a file against the server's upload settings before sending it. Returns null when it may be sent, otherwise
 * `{ code, params }` where code is 'empty', 'tooLarge', 'nameTooLong', or 'unsupported' (an i18n key under upload.reject).
 * An unknown extension with a known MIME type is allowed; the server's type detection is authoritative.
 */
export function precheckFile(file, settings) {
  if (!file) return { code: 'empty', params: {} };
  if (!file.size) return { code: 'empty', params: {} };
  const max = Number(settings?.maxUploadBytes) || 0;
  if (max > 0 && file.size > max) return { code: 'tooLarge', params: { size: formatFileSize(file.size), max: formatFileSize(max) } };
  const maxName = Number(settings?.maxFileNameLength) || 0;
  if (maxName > 0 && String(file.name || '').length > maxName) return { code: 'nameTooLong', params: { max: maxName } };
  const types = Array.isArray(settings?.supportedTypes) ? settings.supportedTypes : [];
  if (types.length > 0) {
    const ext = fileExtension(file.name);
    const mime = String(file.type || '').toLowerCase();
    const known = types.some((t) => (t.extensions || []).includes(ext) || (mime && (t.mimeTypes || []).includes(mime)));
    if (!known) return { code: 'unsupported', params: { ext: ext || '?' } };
  }
  return null;
}

/** The accept attribute for a file input built from the upload settings ('' accepts anything). */
export function acceptAttribute(settings) {
  const types = Array.isArray(settings?.supportedTypes) ? settings.supportedTypes : [];
  const parts = new Set();
  for (const t of types) {
    for (const ext of t.extensions || []) parts.add(`.${ext}`);
    for (const mime of t.mimeTypes || []) parts.add(mime);
  }
  return Array.from(parts).join(',');
}

/** Whether a drag event carries files (as opposed to text or a link being dragged). */
export function dragHasFiles(event) {
  const types = event?.dataTransfer?.types;
  if (!types) return false;
  return Array.from(types).includes('Files');
}

function readEntry(entry, out) {
  return new Promise((resolve) => {
    if (!entry) { resolve(); return; }
    if (entry.isFile) {
      entry.file((f) => { out.push(f); resolve(); }, () => resolve());
      return;
    }
    if (entry.isDirectory) {
      const reader = entry.createReader();
      const batch = () => {
        reader.readEntries(async (entries) => {
          if (!entries.length) { resolve(); return; }
          for (const child of entries) await readEntry(child, out);
          batch();
        }, () => resolve());
      };
      batch();
      return;
    }
    resolve();
  });
}

/** The files a drop carries, expanding dropped folders where the browser supports it. */
export async function filesFromDrop(dataTransfer) {
  if (!dataTransfer) return [];
  const items = dataTransfer.items ? Array.from(dataTransfer.items) : [];
  const entries = items.map((i) => (typeof i.webkitGetAsEntry === 'function' ? i.webkitGetAsEntry() : null));
  if (entries.length > 0 && entries.every(Boolean)) {
    const out = [];
    for (const entry of entries) await readEntry(entry, out);
    return out;
  }
  return Array.from(dataTransfer.files || []);
}

/** Offer a blob to the user as a download under the given file name. */
export function saveBlob(blob, name) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name || 'download';
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Whether a content type is textual enough to show in a text viewer. */
export function isTextContentType(contentType) {
  const ct = String(contentType || '').split(';')[0].trim().toLowerCase();
  if (!ct) return true;
  return ct.startsWith('text/') || ct.includes('json') || ct.includes('xml') || ct.includes('javascript');
}
