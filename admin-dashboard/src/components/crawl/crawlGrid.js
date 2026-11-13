// Shared by the crawl operation and preview modals (kept identical in the admin and subject dashboards): loading
// every object of an operation, and which grid filter each count card applies.

const PAGE_SIZE = 1000;

function itemsOf(resp) {
  if (Array.isArray(resp)) return resp;
  return resp?.objects || resp?.Objects || resp?.items || resp?.data || [];
}

// All of an operation's objects, page by page (the API returns at most 1000 per page), up to `cap`.
export async function fetchAllOperationObjects(apiClient, operationId, cap = 50000) {
  const all = [];
  let skip = 0;
  let total = null;
  for (;;) {
    const resp = await apiClient.listCrawlOperationObjectsPage(operationId, skip, PAGE_SIZE);
    const items = itemsOf(resp);
    if (total === null) total = resp?.totalRecords ?? resp?.TotalRecords ?? null;
    all.push(...items);
    skip += items.length;
    if (items.length < PAGE_SIZE || all.length >= cap || (total !== null && all.length >= total)) break;
  }
  return { items: all.slice(0, cap), total: total ?? all.length, capped: (total ?? all.length) > cap };
}

// Operation counts in two rows: what the crawl found, then what it changed. `filter` is the grid facet a card
// sets ({ column: [values] }); null clears the filters.
export const OPERATION_CARD_ROWS = [
  { key: 'scan', cards: [
    { count: 'enumerated', filter: null },
    { count: 'unchanged', filter: { action: ['Unchanged'] } },
    { count: 'skipped', filter: { action: ['Skip'] } },
    { count: 'missing', filter: { action: ['Missing'] } }
  ] },
  { key: 'changes', cards: [
    { count: 'added', filter: { action: ['Add'] } },
    { count: 'updated', filter: { action: ['Update'] } },
    { count: 'retried', filter: { action: ['Retry'] } },
    { count: 'deleted', filter: { action: ['Delete'] } },
    { count: 'failed', filter: { outcome: ['__failed__'] }, danger: true }
  ] }
];

export const PREVIEW_CARD_ROWS = [
  { key: 'scan', cards: [
    { count: 'enumerated', filter: null },
    { count: 'unchanged', filter: { action: ['Unchanged'] } },
    { count: 'skip', filter: { action: ['Skip'] } },
    { count: 'missing', filter: { action: ['Missing'] } }
  ] },
  { key: 'changes', cards: [
    { count: 'add', filter: { action: ['Add'] } },
    { count: 'update', filter: { action: ['Update'] } },
    { count: 'retry', filter: { action: ['Retry'] } },
    { count: 'delete', filter: { action: ['Delete'] }, danger: true }
  ] }
];

// True when the grid's facets are exactly the card's filter.
export function sameFilter(facets, filter) {
  const active = Object.entries(facets || {}).filter(([, v]) => v && v.length > 0);
  if (!filter) return active.length === 0;
  const wanted = Object.entries(filter);
  return active.length === wanted.length && wanted.every(([k, v]) => {
    const have = facets[k] || [];
    return have.length === v.length && v.every((x) => have.includes(x));
  });
}

export function actionTone(action) {
  if (action === 'Add' || action === 'Update' || action === 'Retry') return 'info';
  if (action === 'Delete' || action === 'Fail') return 'danger';
  if (action === 'Missing') return 'warning';
  return 'neutral';
}
