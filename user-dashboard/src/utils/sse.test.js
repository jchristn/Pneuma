import { describe, it, expect } from 'vitest';
import { parseFrameData } from './sse.js';

describe('parseFrameData', () => {
  it('parses a single data line into an object', () => {
    const event = parseFrameData('data: {"type":"delta","text":"hello"}');
    expect(event).toEqual({ type: 'delta', text: 'hello' });
  });

  it('joins multiple data lines before parsing', () => {
    const event = parseFrameData('data: {"type":"complete",\ndata: "answer":"ok"}');
    expect(event).toEqual({ type: 'complete', answer: 'ok' });
  });

  it('returns null for a frame with no data line', () => {
    expect(parseFrameData(': comment only')).toBeNull();
    expect(parseFrameData('')).toBeNull();
  });

  it('returns null for invalid JSON rather than throwing', () => {
    expect(parseFrameData('data: not json')).toBeNull();
  });

  it('tolerates an optional leading space after the colon', () => {
    expect(parseFrameData('data:{"type":"metadata"}')).toEqual({ type: 'metadata' });
  });
});
