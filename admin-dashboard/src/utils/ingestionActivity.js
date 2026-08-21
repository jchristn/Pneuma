// Shared helpers for the Ingestion Activity chart: the ordered pipeline-stage list, a stable per-stage
// color map (works in light/dark), and normalization of the backend summary into chart buckets.

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

// Normalize the ingestion summary response into an ordered list of buckets, each carrying a
// { stageName: count } map. The backend already gap-fills and orders buckets across the range.
export function normalizeIngestionBuckets(summary) {
  const raw = summary?.buckets || summary?.Buckets || [];
  return raw.map((b) => {
    const stages = {};
    for (const s of (b.stages || b.Stages || [])) {
      const name = s.stage || s.Stage;
      const count = Number(s.count ?? s.Count ?? 0);
      if (name) stages[name] = count;
    }
    return {
      startUtc: b.bucketStartUtc || b.BucketStartUtc,
      endUtc: b.bucketEndUtc || b.BucketEndUtc,
      stages,
      total: Number(b.totalCount ?? b.TotalCount ?? Object.values(stages).reduce((a, c) => a + c, 0))
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
