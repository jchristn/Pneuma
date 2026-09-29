// A status pill with an explicit tone (the shared StatusPill maps link and job statuses only).
function CrawlPill({ label, tone = 'neutral' }) {
  if (label === null || label === undefined || label === '') return <span>-</span>;
  return <span className={`status-pill pill-${tone}`}>{String(label)}</span>;
}

export default CrawlPill;
