/**
 * Minimal server-sent events (SSE) reader built on `fetch` + streams.
 *
 * No EventSource: EventSource cannot send an Authorization header or a POST
 * body, and the Pneuma grounded-chat stream is an authenticated POST. This reads
 * the response body as a stream, splits it into SSE frames, parses each frame's
 * `data:` payload as JSON, and invokes `onEvent` per event. Event objects carry
 * a `type` discriminator: `metadata`, `delta`, `complete`, or `error`.
 */

export function parseFrameData(frame) {
  const lines = frame.split('\n');
  const dataParts = [];
  for (const line of lines) {
    if (line.startsWith('data:')) dataParts.push(line.slice(5).replace(/^ /, ''));
  }
  if (dataParts.length === 0) return null;
  try {
    return JSON.parse(dataParts.join('\n'));
  } catch {
    return null;
  }
}

/**
 * Open an SSE stream and dispatch parsed events until the stream ends.
 * @param {string} url Absolute URL.
 * @param {object} options
 * @param {string} [options.method] HTTP method (default POST).
 * @param {object} [options.headers] Request headers.
 * @param {object|null} [options.body] JSON body (serialized).
 * @param {AbortSignal} [options.signal] Abort signal to cancel the stream.
 * @param {(event:object)=>void} [options.onEvent] Called per parsed event.
 */
export async function streamSse(url, { method = 'POST', headers = {}, body = null, signal, onEvent } = {}) {
  const response = await fetch(url, {
    method,
    headers,
    body: body !== null && body !== undefined ? JSON.stringify(body) : undefined,
    signal,
  });

  if (response.status === 401) {
    window.dispatchEvent(new CustomEvent('pneuma:unauthorized'));
  }

  if (!response.ok || !response.body) {
    const text = await response.text().catch(() => '');
    const error = new Error(text || `HTTP ${response.status}`);
    error.status = response.status;
    throw error;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let boundary;
    while ((boundary = buffer.indexOf('\n\n')) >= 0) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const data = parseFrameData(frame);
      if (data && onEvent) onEvent(data);
    }
  }

  const tail = parseFrameData(buffer);
  if (tail && onEvent) onEvent(tail);
}

export default streamSse;
