// Shared helpers for the Ingestion Activity chart: the ordered pipeline-stage list, a stable per-stage
// color map (works in light/dark), and normalization of the backend summary into chart buckets.

import { RANGES } from '../components/ActivityChart';

// Pipeline stages in execution order (mirrors the backend IngestionStageEnum). This is also the
// bottom-to-top stacking order for the stacked bars and the legend order.
export const INGESTION_STAGES = [
  'Pending', 'ContentRetrieval', 'TypeDetection', 'CellExtraction', 'Classification',
  'Categorization', 'Hydration', 'GraphMerge', 'Summarization', 'Chunking', 'Embedding', 'Indexing', 'Done'
];

// Distinct, theme-neutral hues per stage. Kept explicit (not generated) so a stage keeps its color
// across renders and between the two dashboards.
export const STAGE_COLORS = {
  Pending: '#94a3b8',
  ContentRetrieval: '#0ea5e9',
  TypeDetection: '#6366f1',
  CellExtraction: '#8b5cf6',
  Classification: '#a855f7',
  Categorization: '#ec4899',
  Hydration: '#f59e0b',
  GraphMerge: '#14b8a6',
  Summarization: '#10b981',
  Chunking: '#84cc16',
  Embedding: '#3b82f6',
  Indexing: '#f97316',
  Done: '#22c55e'
};

export function stageColor(stage) {
  return STAGE_COLORS[stage] || '#64748b';
}

// Humanize a PascalCase stage name for labels/legends ("ContentRetrieval" -> "Content Retrieval").
export function stageLabel(stage) {
  return String(stage || '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

// Normalize the ingestion summary into a fixed-width grid for the selected range — the SAME grid the
// Request Activity chart uses (RANGES[rangeId].stepMs across the range's window) — so both charts always
// render the same number of buckets for a given range, regardless of how the backend bucketed the data.
// Backend buckets are snapped into the grid cell they fall into and their per-stage counts summed.
export function normalizeIngestionBuckets(summary, rangeId = 'day') {
  const range = RANGES[rangeId] || RANGES.day;
  const step = range.stepMs;
  const end = Date.now();
  const start = end - range.hours * 3600 * 1000;
  const raw = summary?.buckets || summary?.Buckets || [];

  const cells = new Map();
  for (const b of raw) {
    const ts = b.bucketStartUtc || b.BucketStartUtc;
    if (!ts) continue;
    const key = Math.floor(new Date(ts).getTime() / step) * step;
    let cell = cells.get(key);
    if (!cell) { cell = {}; cells.set(key, cell); }
    for (const s of (b.stages || b.Stages || [])) {
      const name = s.stage || s.Stage;
      const count = Number(s.count ?? s.Count ?? 0);
      if (name) cell[name] = (cell[name] || 0) + count;
    }
  }

  const out = [];
  for (let t = Math.floor(start / step) * step; t < end; t += step) {
    const stages = cells.get(t) || {};
    out.push({
      startUtc: new Date(t).toISOString(),
      endUtc: new Date(t + step).toISOString(),
      stages,
      total: Object.values(stages).reduce((a, c) => a + c, 0)
    });
  }
  return out;
}

// The stages that actually occur across the buckets, in canonical order — drives stacking + legend.
export function stagesPresent(buckets) {
  const present = new Set();
  for (const b of buckets) {
    for (const [name, count] of Object.entries(b.stages)) {
      if (count > 0) present.add(name);
    }
  }
  return INGESTION_STAGES.filter((s) => present.has(s));
}

// Overall per-stage totals from the summary (for the stat row), ordered canonically.
export function ingestionTotals(summary) {
  const totals = {};
  for (const s of (summary?.totals || summary?.Totals || [])) {
    const name = s.stage || s.Stage;
    if (name) totals[name] = Number(s.count ?? s.Count ?? 0);
  }
  const total = Number(summary?.totalCount ?? summary?.TotalCount ?? Object.values(totals).reduce((a, c) => a + c, 0));
  return { total, byStage: totals };
}
