// Shared helpers for the Ingestion Activity chart: the ordered pipeline-stage list, a stable per-stage
// color map (works in light/dark), and normalization of the backend summary into chart buckets.

import { getRangeWindow } from './activity';
import i18n from '../i18n';

// Pipeline stages in execution order (mirrors the backend IngestionStageEnum). Also the bottom-to-top
// stacking order for the stacked bars and the legend order.
export const INGESTION_STAGES = [
  'Pending', 'ContentRetrieval', 'TypeDetection', 'CellExtraction', 'Classification',
  'Categorization', 'Hydration', 'OntologyCanonicalization', 'GraphMerge', 'RelationshipConsolidation',
  'Summarization', 'Chunking', 'Embedding', 'Indexing', 'Done'
];

// Distinct, theme-neutral hues per stage, kept explicit so a stage keeps its color across renders.
export const STAGE_COLORS = {
  Pending: '#94a3b8',
  ContentRetrieval: '#0ea5e9',
  TypeDetection: '#6366f1',
  CellExtraction: '#8b5cf6',
  Classification: '#a855f7',
  Categorization: '#ec4899',
  Hydration: '#f59e0b',
  OntologyCanonicalization: '#2dd4bf',
  GraphMerge: '#14b8a6',
  RelationshipConsolidation: '#0d9488',
  Summarization: '#10b981',
  Chunking: '#84cc16',
  Embedding: '#3b82f6',
  Indexing: '#f97316',
  Done: '#22c55e'
};

export function stageColor(stage) {
  return STAGE_COLORS[stage] || '#64748b';
}

// Canonical stage -> i18n key map. Covers every IngestionStageEnum value with a distinct label so no two
// stages read the same. Keyed by the stage name normalized to lowercase with separators stripped, so it
// matches regardless of how the backend cases/spaces the raw value.
export const STAGE_LABEL_KEYS = {
  pending: 'ingestionStages.pending',
  contentretrieval: 'ingestionStages.contentRetrieval',
  typedetection: 'ingestionStages.typeDetection',
  cellextraction: 'ingestionStages.cellExtraction',
  classification: 'ingestionStages.classification',
  categorization: 'ingestionStages.categorization',
  hydration: 'ingestionStages.hydration',
  ontologycanonicalization: 'ingestionStages.ontologyCanonicalization',
  graphmerge: 'ingestionStages.graphMerge',
  relationshipconsolidation: 'ingestionStages.relationshipConsolidation',
  summarization: 'ingestionStages.summarization',
  chunking: 'ingestionStages.chunking',
  embedding: 'ingestionStages.embedding',
  indexing: 'ingestionStages.indexing',
  done: 'ingestionStages.done'
};

// Normalize a stage value for map lookup ("Content Retrieval"/"content_retrieval" -> "contentretrieval").
function normalizeStageKey(value) {
  return String(value ?? '').toLowerCase().replace(/[\s_-]/g, '');
}

// Humanize an unknown/raw stage name for display ("ContentRetrieval" -> "Content Retrieval"). Used only as
// a fallback when the value isn't a known stage, so a stray value never renders blank.
function humanizeStage(stage) {
  return String(stage || '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim();
}

// Resolve a backend stage value to its distinct, human-readable label via i18next. Unknown values fall
// back to a humanized form of the raw string (never blank); empty values render as an em dash.
export function stageLabel(stage) {
  if (stage === null || stage === undefined || stage === '') return '—';
  const key = STAGE_LABEL_KEYS[normalizeStageKey(stage)];
  if (key) return i18n.t(key);
  const human = humanizeStage(stage);
  return human || String(stage);
}

// Normalize the ingestion summary into a fixed-width grid for the selected range — the SAME window/slice
// grid the Request Activity chart uses (getRangeWindow: bucketMs × sliceCount) — so both charts always
// render the same number of buckets for a given range, regardless of how the backend bucketed the data.
// Backend buckets are snapped into the grid cell they fall into and their per-stage counts summed.
export function normalizeIngestionBuckets(summary, rangeId = 'day') {
  const range = getRangeWindow(rangeId);
  const raw = summary?.buckets || summary?.Buckets || [];

  const cells = new Map();
  for (const b of raw) {
    const ts = b.bucketStartUtc || b.BucketStartUtc;
    if (!ts) continue;
    const key = Math.floor(new Date(ts).getTime() / range.bucketMs) * range.bucketMs;
    let cell = cells.get(key);
    if (!cell) { cell = {}; cells.set(key, cell); }
    for (const s of (b.stages || b.Stages || [])) {
      const name = s.stage || s.Stage;
      const count = Number(s.count ?? s.Count ?? 0);
      if (name) cell[name] = (cell[name] || 0) + count;
    }
  }

  return Array.from({ length: range.sliceCount }, (_, index) => {
    const startMs = range.startMs + index * range.bucketMs;
    const stages = cells.get(startMs) || {};
    return {
      startUtc: new Date(startMs).toISOString(),
      endUtc: new Date(startMs + range.bucketMs).toISOString(),
      stages,
      total: Object.values(stages).reduce((a, c) => a + c, 0)
    };
  });
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
