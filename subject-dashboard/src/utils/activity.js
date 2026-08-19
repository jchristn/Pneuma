/**
 * Shared helpers for the request activity chart. Range presets match the
 * backend's supported intervals (see FRONTEND_ARCHITECTURE.md).
 */

export const ACTIVITY_RANGES = [
  { id: 'hour', labelKey: 'Last Hour', bucketMinutes: 1, sliceCount: 60 },
  { id: 'day', labelKey: 'Last Day', bucketMinutes: 15, sliceCount: 96 },
  { id: 'week', labelKey: 'Last Week', bucketMinutes: 120, sliceCount: 84 },
  { id: 'month', labelKey: 'Last Month', bucketMinutes: 360, sliceCount: 120 }
];

export function getRangeConfig(rangeId) {
  return ACTIVITY_RANGES.find((r) => r.id === rangeId) || ACTIVITY_RANGES[1];
}

export function getRangeWindow(rangeId, now = new Date()) {
  const config = getRangeConfig(rangeId);
  const bucketMs = config.bucketMinutes * 60 * 1000;
  const endExclusiveMs = Math.floor(now.getTime() / bucketMs) * bucketMs + bucketMs;
  const startMs = endExclusiveMs - config.sliceCount * bucketMs;
  return {
    ...config,
    bucketMs,
    startMs,
    endExclusiveMs,
    startUtc: new Date(startMs),
    endUtc: new Date(endExclusiveMs - 1)
  };
}

export function buildRangeParams(rangeId, now = new Date()) {
  const range = getRangeWindow(rangeId, now);
  return {
    bucketMinutes: range.bucketMinutes,
    fromUtc: range.startUtc.toISOString(),
    toUtc: range.endUtc.toISOString()
  };
}

// Extract bucket fields defensively across possible backend field names.
function readBucket(raw) {
  const start =
    raw.bucketStartUtc || raw.timestampUtc || raw.startUtc || raw.bucketStart || raw.timestamp;
  const total = raw.totalCount ?? raw.total ?? raw.count ?? 0;
  const success = raw.successCount ?? raw.success ?? 0;
  const failure = raw.failureCount ?? raw.failure ?? raw.errorCount ?? 0;
  const avg = raw.averageDurationMs ?? raw.avgDurationMs ?? raw.averageMs ?? 0;
  return {
    start,
    total: total || success + failure,
    success,
    failure,
    avg
  };
}

function extractBuckets(summary) {
  if (!summary) return [];
  if (Array.isArray(summary)) return summary;
  return summary.buckets || summary.data || summary.items || summary.samples || [];
}

/**
 * Normalize a summary response into a fixed-length array of buckets (fills gaps
 * with zero counts).
 */
export function normalizeBuckets(summary, range) {
  const apiBuckets = new Map();
  for (const raw of extractBuckets(summary)) {
    const b = readBucket(raw);
    if (!b.start) continue;
    const key = Math.floor(new Date(b.start).getTime() / range.bucketMs) * range.bucketMs;
    apiBuckets.set(key, b);
  }

  return Array.from({ length: range.sliceCount }, (_, index) => {
    const startMs = range.startMs + index * range.bucketMs;
    const match = apiBuckets.get(startMs);
    return {
      bucketStartUtc: new Date(startMs).toISOString(),
      bucketEndUtc: new Date(startMs + range.bucketMs).toISOString(),
      totalCount: match?.total || 0,
      successCount: match?.success || 0,
      failureCount: match?.failure || 0,
      averageDurationMs: match?.avg || 0
    };
  });
}

/**
 * Aggregate totals from a summary (used for KPI-ish captions).
 */
export function summaryTotals(summary) {
  if (!summary) return { total: 0, success: 0, failure: 0, avg: 0 };
  const total =
    summary.totalCount ?? summary.total ?? summary.totalRequests ?? null;
  if (total != null) {
    return {
      total,
      success: summary.successCount ?? summary.totalSuccess ?? summary.success ?? 0,
      failure: summary.failureCount ?? summary.totalFailure ?? summary.failure ?? 0,
      avg: summary.averageDurationMs ?? summary.avgDurationMs ?? 0
    };
  }
  // derive from buckets
  const buckets = extractBuckets(summary).map(readBucket);
  return buckets.reduce(
    (acc, b) => ({
      total: acc.total + b.total,
      success: acc.success + b.success,
      failure: acc.failure + b.failure,
      avg: 0
    }),
    { total: 0, success: 0, failure: 0, avg: 0 }
  );
}
