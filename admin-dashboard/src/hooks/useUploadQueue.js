import { useCallback, useEffect, useRef, useState } from 'react';
import { precheckFile } from '../utils/files';

/** Files uploaded at once; the rest wait in the queue. */
export const UPLOAD_CONCURRENCY = 3;

let nextId = 1;

/**
 * Upload queue for the file upload modal. Each item is
 * `{ id, file, status, loaded, error, result, reject }` where status is
 * queued | uploading | done | rejected | failed | cancelled. `reject` is a client-side pre-check failure
 * (`{ code, params }`, never sent). `start(send)` runs queued items through `send(item, onProgress, signal)` with at
 * most UPLOAD_CONCURRENCY in flight; failed items can be retried and any item that is not uploading can be removed.
 */
export function useUploadQueue(settings) {
  const [items, setItems] = useState([]);
  const controllers = useRef(new Map());
  const sendRef = useRef(null);
  const itemsRef = useRef(items);
  itemsRef.current = items;

  const update = useCallback((id, patch) => {
    setItems((list) => list.map((it) => (it.id === id ? { ...it, ...patch } : it)));
  }, []);

  const add = useCallback((files) => {
    const incoming = Array.from(files || []).map((file) => {
      const reject = precheckFile(file, settings);
      return { id: nextId++, file, status: reject ? 'rejected' : 'queued', loaded: 0, error: '', result: null, reject };
    });
    if (incoming.length) setItems((list) => [...list, ...incoming]);
  }, [settings]);

  // Re-run the pre-check when the settings arrive after files were already added.
  useEffect(() => {
    if (!settings) return;
    setItems((list) => list.map((it) => {
      if (it.status !== 'queued' && it.status !== 'rejected') return it;
      const reject = precheckFile(it.file, settings);
      return { ...it, reject, status: reject ? 'rejected' : 'queued' };
    }));
  }, [settings]);

  const remove = useCallback((id) => {
    controllers.current.get(id)?.abort();
    controllers.current.delete(id);
    setItems((list) => list.filter((it) => it.id !== id));
  }, []);

  const retry = useCallback((id) => update(id, { status: 'queued', loaded: 0, error: '', result: null }), [update]);

  const clearFinished = useCallback(() => {
    setItems((list) => list.filter((it) => it.status === 'queued' || it.status === 'uploading'));
  }, []);

  const reset = useCallback(() => {
    for (const c of controllers.current.values()) c.abort();
    controllers.current.clear();
    sendRef.current = null;
    setItems([]);
  }, []);

  const cancelAll = useCallback(() => {
    for (const c of controllers.current.values()) c.abort();
    controllers.current.clear();
    sendRef.current = null;
    setItems((list) => list.map((it) => (it.status === 'queued' || it.status === 'uploading' ? { ...it, status: 'cancelled' } : it)));
  }, []);

  const start = useCallback((send) => {
    sendRef.current = send;
    setItems((list) => [...list]);
  }, []);

  // Pump: whenever the list changes and a sender is active, start queued items up to the concurrency limit.
  useEffect(() => {
    const send = sendRef.current;
    if (!send) return;
    const running = items.filter((it) => it.status === 'uploading').length;
    const queued = items.filter((it) => it.status === 'queued');
    if (running === 0 && queued.length === 0) {
      // The run is over: files added from now on wait for the next start.
      sendRef.current = null;
      return;
    }
    const free = UPLOAD_CONCURRENCY - running;
    if (free <= 0) return;
    const next = queued.slice(0, free);
    for (const item of next) {
      const controller = new AbortController();
      controllers.current.set(item.id, controller);
      update(item.id, { status: 'uploading', loaded: 0, error: '' });
      send(item, (loaded) => update(item.id, { loaded }), controller.signal)
        .then((result) => update(item.id, { status: 'done', loaded: item.file.size, result }))
        .catch((err) => {
          if (controller.signal.aborted) return;
          update(item.id, { status: 'failed', error: err?.message || String(err), errorStatus: err?.status || 0 });
        })
        .finally(() => controllers.current.delete(item.id));
    }
  }, [items, update]);

  useEffect(() => () => {
    for (const c of controllers.current.values()) c.abort();
  }, []);

  const counts = items.reduce((acc, it) => {
    acc[it.status] = (acc[it.status] || 0) + 1;
    return acc;
  }, {});

  return {
    items,
    counts,
    busy: (counts.uploading || 0) > 0,
    pending: (counts.queued || 0) + (counts.uploading || 0),
    add,
    remove,
    retry,
    start,
    reset,
    cancelAll,
    clearFinished
  };
}
