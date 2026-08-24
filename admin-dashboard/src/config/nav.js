// Grouped navigation model. Labels are i18n keys resolved in the Sidebar.
export const NAV_GROUPS = [
  {
    labelKey: 'nav.groupOverview',
    items: [
      { section: 'home', labelKey: 'nav.home', icon: 'home', tip: 'Dashboard overview: key counts, recent activity, and system health at a glance.' }
    ]
  },
  {
    labelKey: 'nav.groupContent',
    items: [
      { section: 'subjects', labelKey: 'nav.subjects', icon: 'users', tip: 'Subjects are the top-level things your archives are about. Everything you ingest is scoped to one.' },
      { section: 'links', labelKey: 'nav.links', icon: 'link', tip: 'Source links (URLs/documents) submitted for ingestion into a subject, with their status.' },
      { section: 'collections', labelKey: 'nav.collections', icon: 'database', tip: 'RecallDB vector collections that store embedded chunks for retrieval. Dimensionality is fixed at creation.' },
      { section: 'search', labelKey: 'nav.search', icon: 'search', tip: 'Full-text search a subject’s ingested documents and jump to the matching chunks.' },
      { section: 'ask', labelKey: 'nav.ask', icon: 'chat', tip: 'Chat with the corpus. The assistant retrieves grounded facts and cites its sources.' },
      { section: 'conversations', labelKey: 'nav.conversations', icon: 'chat', tip: 'Every saved conversation across subjects, with open, rename, and delete.' },
      { section: 'jobs', labelKey: 'nav.jobs', icon: 'queue', tip: 'The live ingestion queue: jobs currently pending or processing, with controls to stop or retry.' },
      { section: 'ingestion-jobs', labelKey: 'nav.ingestionJobs', icon: 'list', tip: 'Full history of ingestion jobs across all subjects, including completed and failed runs.' },
      { section: 'history', labelKey: 'nav.history', icon: 'chat', tip: 'Every chat turn across subjects, with the full question, answer, timing, and metadata.' },
      { section: 'analytics', labelKey: 'nav.analytics', icon: 'chart', tip: 'Per-subject chat volume, latency percentiles, per-stage timing, and feedback over time.' },
      { section: 'eval', labelKey: 'nav.eval', icon: 'list', tip: 'Grade the assistant against ground-truth facts: manage facts, run LLM-judged evaluations, and review results.' },
      { section: 'feedback', labelKey: 'nav.feedback', icon: 'chart', tip: 'Thumbs up/down and comments users left on chat answers, with the rated prompt and response.' }
    ]
  },
  {
    labelKey: 'nav.groupAdministration',
    items: [
      { section: 'tenants', labelKey: 'nav.tenants', icon: 'building', tip: 'Tenants isolate data and users. Each maps to its own RecallDB and LiteGraph tenant.' },
      { section: 'users', labelKey: 'nav.users', icon: 'user', tip: 'People who can sign in to a dashboard, with their roles and status.' },
      { section: 'credentials', labelKey: 'nav.credentials', icon: 'key', tip: 'API key credentials (access/secret pairs) for programmatic access, with their scopes.' },
      { section: 'roles', labelKey: 'nav.roles', icon: 'shield', tip: 'Named bundles of permissions you assign to users to grant access.' },
      { section: 'permissions', labelKey: 'nav.permissions', icon: 'lock', tip: 'The individual allow/deny rules (resource + operation) that roles are built from.' },
      { section: 'assignments', labelKey: 'nav.assignments', icon: 'link', tip: 'Which roles are granted to which users, and which permissions each role maps to.' },
      { section: 'audit', labelKey: 'nav.audit', icon: 'list', tip: 'Immutable log of security events: logins, authorization decisions, and admin bypasses.' }
    ]
  },
  {
    labelKey: 'nav.groupConfiguration',
    items: [
      { section: 'model-runners', labelKey: 'nav.modelRunners', icon: 'cpu', tip: 'Embedding and completion model endpoints (proxied to Partio) that power ingestion and answering.' },
      { section: 'prompts', labelKey: 'nav.prompts', icon: 'chat', tip: 'System and ingestion prompt templates the models use. Edit to tune tone and behavior.' }
    ]
  },
  {
    labelKey: 'nav.groupObservability',
    items: [
      { section: 'requests', labelKey: 'nav.requests', icon: 'chart', tip: 'Captured API request history with an inspector — method, status, timing, and redacted bodies.' },
      { section: 'explorer', labelKey: 'nav.explorer', icon: 'play', tip: 'Interactive OpenAPI explorer to build and run API calls against this server.' }
    ]
  },
  {
    labelKey: 'nav.groupSystem',
    items: [
      { section: 'settings', labelKey: 'nav.settings', icon: 'gear', tip: 'Server configuration: retrieval tuning, integrations, request capture, and more.' }
    ]
  }
];

// Flat lookup: section -> { labelKey, titleKey, subtitleKey }
export const SECTION_META = {
  home: { titleKey: 'home.title', subtitleKey: 'home.subtitle' },
  subjects: { titleKey: 'nav.subjects' },
  links: { titleKey: 'nav.links' },
  collections: { titleKey: 'collections.title', subtitleKey: 'collections.subtitle' },
  search: { titleKey: 'search.title', subtitleKey: 'search.subtitle' },
  ask: { titleKey: 'nav.ask' },
  conversations: { titleKey: 'nav.conversations' },
  jobs: { titleKey: 'jobs.title', subtitleKey: 'jobs.subtitle' },
  'ingestion-jobs': { titleKey: 'jobs.jobsTitle', subtitleKey: 'jobs.jobsSubtitle' },
  tenants: { titleKey: 'nav.tenants' },
  users: { titleKey: 'nav.users' },
  credentials: { titleKey: 'nav.credentials' },
  roles: { titleKey: 'nav.roles' },
  permissions: { titleKey: 'nav.permissions' },
  assignments: { titleKey: 'nav.assignments' },
  audit: { titleKey: 'nav.audit' },
  history: { titleKey: 'history.title', subtitleKey: 'history.subtitle' },
  feedback: { titleKey: 'feedback.title', subtitleKey: 'feedback.subtitle' },
  'model-runners': { titleKey: 'modelRunners.title', subtitleKey: 'modelRunners.subtitle' },
  prompts: { titleKey: 'prompts.title', subtitleKey: 'prompts.subtitle' },
  requests: { titleKey: 'requests.title', subtitleKey: 'requests.subtitle' },
  explorer: { titleKey: 'explorer.title', subtitleKey: 'explorer.subtitle' },
  settings: { titleKey: 'settings.title', subtitleKey: 'settings.subtitle' }
};
