// The dashboard-tunable ingestion concurrency knobs, grouped for display. These field keys mirror the
// backend IngestionTuning contract exactly and are shared by the admin Processing (system-defaults) page
// and the per-subject concurrency-overrides editor. Labels/tips are resolved via i18n keys
// (`processing.fields.<key>` / `processing.tips.<key>`).
export const INGESTION_TUNABLE_GROUPS = [
  {
    key: 'perStage',
    fields: [
      'contentRetrieval',
      'typeDetection',
      'cellExtraction',
      'classification',
      'graphMerge',
      'summarization',
      'chunking',
      'embedding',
      'indexing'
    ]
  },
  {
    key: 'jobPool',
    fields: ['maxConcurrentTasks', 'summarizationConcurrency', 'summarizationMinCellLength']
  },
  {
    key: 'timeouts',
    fields: ['stageTimeoutSeconds']
  }
];

// Flat list of every tunable key, in display order.
export const INGESTION_TUNABLE_KEYS = INGESTION_TUNABLE_GROUPS.reduce(
  (all, group) => all.concat(group.fields),
  []
);

export default INGESTION_TUNABLE_GROUPS;
