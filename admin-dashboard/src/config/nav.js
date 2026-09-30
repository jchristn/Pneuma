// Grouped navigation model. The admin dashboard is organized into a small set of top-level workspaces; each
// workspace opens a page with a tab strip whose tabs are the individual views. Sidebar labels/tooltips are
// i18n keys resolved in the Sidebar; tab labels are i18n keys resolved in the tab strip.
//
// `WORKSPACE_TABS[section]` lists the tabs for a workspace. Each tab's `view` is a key into the Dashboard's
// VIEWS component map (the same keys the app used before the consolidation), so the underlying view components
// are reused unchanged — they simply render inside a tab. `adminOnly` tabs are hidden from non-system-admins.
export const WORKSPACE_TABS = {
  home: [
    { key: 'home', view: 'home', labelKey: 'nav.home' }
  ],
  knowledge: [
    { key: 'subjects', view: 'subjects', labelKey: 'nav.subjects' },
    { key: 'collections', view: 'collections', labelKey: 'nav.collections' },
    { key: 'ontologies', view: 'ontologies', labelKey: 'nav.ontologies' },
    { key: 'search', view: 'search', labelKey: 'nav.search' }
  ],
  // Source material: the links a subject is built from, and the crawl plans (and their runs) that keep links in
  // sync with a web site, bucket, share, repository, or folder.
  sources: [
    { key: 'links', view: 'links', labelKey: 'nav.links' },
    { key: 'crawl-plans', view: 'crawl-plans', labelKey: 'nav.crawlPlans' },
    { key: 'crawl-operations', view: 'crawl-operations', labelKey: 'nav.crawlOperations' }
  ],
  ingestion: [
    { key: 'live', view: 'ingestion-live', labelKey: 'nav.ingestionLive' },
    { key: 'queue', view: 'jobs', labelKey: 'nav.jobs' },
    { key: 'jobs', view: 'ingestion-jobs', labelKey: 'nav.ingestionJobs' }
  ],
  assistant: [
    { key: 'ask', view: 'ask', labelKey: 'nav.ask' },
    { key: 'conversations', view: 'conversations', labelKey: 'nav.conversations' },
    { key: 'history', view: 'history', labelKey: 'nav.history' },
    { key: 'feedback', view: 'feedback', labelKey: 'nav.feedback' },
    { key: 'eval', view: 'eval', labelKey: 'nav.eval' },
    { key: 'analytics', view: 'analytics', labelKey: 'nav.analytics' }
  ],
  access: [
    { key: 'tenants', view: 'tenants', labelKey: 'nav.tenants' },
    { key: 'users', view: 'users', labelKey: 'nav.users' },
    { key: 'credentials', view: 'credentials', labelKey: 'nav.credentials' },
    { key: 'roles', view: 'roles', labelKey: 'nav.roles' },
    { key: 'permissions', view: 'permissions', labelKey: 'nav.permissions' },
    { key: 'assignments', view: 'assignments', labelKey: 'nav.assignments' },
    { key: 'audit', view: 'audit', labelKey: 'nav.audit' }
  ],
  models: [
    { key: 'endpoints', view: 'model-runners', labelKey: 'nav.modelRunners' },
    { key: 'prompts', view: 'prompts', labelKey: 'nav.prompts' }
  ],
  system: [
    { key: 'settings', view: 'settings', labelKey: 'nav.settings' },
    { key: 'processing', view: 'processing', labelKey: 'nav.processing', adminOnly: true },
    { key: 'requests', view: 'requests', labelKey: 'nav.requests' },
    { key: 'explorer', view: 'explorer', labelKey: 'nav.explorer' }
  ]
};

// The sidebar: eight workspaces in two groups, in the order an operator works: define a subject, add its sources,
// watch them ingest, then ask. Each item's `section` selects a workspace in WORKSPACE_TABS.
export const NAV_GROUPS = [
  {
    labelKey: 'nav.groupWorkspace',
    items: [
      { section: 'home', labelKey: 'nav.home', icon: 'home', tip: 'Dashboard overview: key counts, recent activity, and system health at a glance.' },
      { section: 'knowledge', labelKey: 'nav.knowledge', icon: 'database', tip: 'The knowledge base: subjects, the retrieval collections, the ontologies subjects classify into, and full-text search.' },
      { section: 'sources', labelKey: 'nav.sources', icon: 'link', tip: 'Source material: the links each subject is built from, and the crawl plans that keep them in sync with web sites, repositories, buckets, shares, and folders.' },
      { section: 'ingestion', labelKey: 'nav.ingestion', icon: 'queue', tip: 'The ingestion pipeline: the live view, the queue, and full job history.' },
      { section: 'assistant', labelKey: 'nav.assistant', icon: 'chat', tip: 'Chat with the corpus, browse conversations and history, review feedback, and evaluate answer quality.' }
    ]
  },
  {
    labelKey: 'nav.groupAdministration',
    items: [
      { section: 'access', labelKey: 'nav.access', icon: 'shield', tip: 'Identity and access: tenants, users, credentials, roles, permissions, assignments, and the audit log.' },
      { section: 'models', labelKey: 'nav.models', icon: 'cpu', tip: 'Model endpoints and the prompt templates the pipeline uses.' },
      { section: 'system', labelKey: 'nav.system', icon: 'gear', tip: 'Server configuration, ingestion tuning, request history, and the API explorer.' }
    ]
  }
];

// Per-workspace page metadata (used for the browser tab title).
export const SECTION_META = {
  home: { titleKey: 'home.title', subtitleKey: 'home.subtitle' },
  knowledge: { titleKey: 'nav.knowledge' },
  sources: { titleKey: 'nav.sources' },
  ingestion: { titleKey: 'nav.ingestion' },
  assistant: { titleKey: 'nav.assistant' },
  access: { titleKey: 'nav.access' },
  models: { titleKey: 'nav.models' },
  system: { titleKey: 'nav.system' }
};

// Old single-view section keys → their new { workspace section, tab } home, so existing bookmarks and in-app
// links (e.g. /dashboard/subjects, /dashboard/jobs) redirect to the consolidated tabbed location.
export const LEGACY_REDIRECTS = {
  subjects: { section: 'knowledge', tab: 'subjects' },
  links: { section: 'sources', tab: 'links' },
  'crawl-plans': { section: 'sources', tab: 'crawl-plans' },
  'crawl-operations': { section: 'sources', tab: 'crawl-operations' },
  collections: { section: 'knowledge', tab: 'collections' },
  ontologies: { section: 'knowledge', tab: 'ontologies' },
  search: { section: 'knowledge', tab: 'search' },
  'ingestion-live': { section: 'ingestion', tab: 'live' },
  jobs: { section: 'ingestion', tab: 'queue' },
  'ingestion-jobs': { section: 'ingestion', tab: 'jobs' },
  ask: { section: 'assistant', tab: 'ask' },
  conversations: { section: 'assistant', tab: 'conversations' },
  history: { section: 'assistant', tab: 'history' },
  feedback: { section: 'assistant', tab: 'feedback' },
  eval: { section: 'assistant', tab: 'eval' },
  analytics: { section: 'assistant', tab: 'analytics' },
  tenants: { section: 'access', tab: 'tenants' },
  users: { section: 'access', tab: 'users' },
  credentials: { section: 'access', tab: 'credentials' },
  roles: { section: 'access', tab: 'roles' },
  permissions: { section: 'access', tab: 'permissions' },
  assignments: { section: 'access', tab: 'assignments' },
  audit: { section: 'access', tab: 'audit' },
  'model-runners': { section: 'models', tab: 'endpoints' },
  prompts: { section: 'models', tab: 'prompts' },
  settings: { section: 'system', tab: 'settings' },
  processing: { section: 'system', tab: 'processing' },
  requests: { section: 'system', tab: 'requests' },
  explorer: { section: 'system', tab: 'explorer' }
};

// Tabs that moved to another workspace: { section: { tab: newSection } }. /dashboard/<section>/<tab> redirects to
// /dashboard/<newSection>/<tab>, so bookmarks from before Sources was split out keep working.
export const MOVED_TABS = {
  knowledge: { links: 'sources' },
  ingestion: { 'crawl-plans': 'sources', 'crawl-operations': 'sources' }
};
