import { useEffect, useRef, useState } from 'react';
import { dragHasFiles, filesFromDrop } from '../utils/files';

/**
 * Accept files dropped anywhere on the page while `enabled`. Returns `dragging` (true while files are dragged over
 * the window, to show an overlay) and calls `onFiles(files)` on drop. Drags of text or links are ignored. A drop
 * that a nested drop zone already handled (it calls preventDefault and stopPropagation) does not reach this hook.
 */
export function usePageFileDrop(enabled, onFiles) {
  const [dragging, setDragging] = useState(false);
  const depth = useRef(0);
  const onFilesRef = useRef(onFiles);
  onFilesRef.current = onFiles;

  useEffect(() => {
    if (!enabled) {
      depth.current = 0;
      setDragging(false);
      return undefined;
    }

    const onEnter = (e) => {
      if (!dragHasFiles(e)) return;
      e.preventDefault();
      depth.current += 1;
      setDragging(true);
    };
    const onOver = (e) => {
      if (!dragHasFiles(e)) return;
      e.preventDefault();
      if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
    };
    const onLeave = (e) => {
      if (!dragHasFiles(e)) return;
      depth.current = Math.max(0, depth.current - 1);
      if (depth.current === 0) setDragging(false);
    };
    const onDrop = async (e) => {
      if (!dragHasFiles(e)) return;
      e.preventDefault();
      depth.current = 0;
      setDragging(false);
      const files = await filesFromDrop(e.dataTransfer);
      if (files.length) onFilesRef.current?.(files);
    };

    window.addEventListener('dragenter', onEnter);
    window.addEventListener('dragover', onOver);
    window.addEventListener('dragleave', onLeave);
    window.addEventListener('drop', onDrop);
    return () => {
      window.removeEventListener('dragenter', onEnter);
      window.removeEventListener('dragover', onOver);
      window.removeEventListener('dragleave', onLeave);
      window.removeEventListener('drop', onDrop);
    };
  }, [enabled]);

  return dragging;
}
