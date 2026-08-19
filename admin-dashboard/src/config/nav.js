// Grouped navigation model. Labels are i18n keys resolved in the Sidebar.
export const NAV_GROUPS = [
  {
    labelKey: 'nav.groupOverview',
    items: [
      { section: 'home', labelKey: 'nav.home', icon: 'home' }
    ]
  },
  {
    labelKey: 'nav.groupContent',
    items: [
      { section: 'subjects', labelKey: 'nav.subjects', icon: 'users' },
      { section: 'links', labelKey: 'nav.links', icon: 'link' },
      { section: 'collections', labelKey: 'nav.collections', icon: 'database' },
      { section: 'search', labelKey: 'nav.search', icon: 'search' },
      { section: 'ask', labelKey: 'nav.ask', icon: 'chat' },
      { section: 'jobs', labelKey: 'nav.jobs', icon: 'queue' },
      { section: 'ingestion-jobs', labelKey: 'nav.ingestionJobs', icon: 'list' }
    ]
  },
  {
    labelKey: 'nav.groupAdministration',
    items: [
      { section: 'tenants', labelKey: 'nav.tenants', icon: 'building' },
      { section: 'users', labelKey: 'nav.users', icon: 'user' },
      { section: 'credentials', labelKey: 'nav.credentials', icon: 'key' },
      { section: 'roles', labelKey: 'nav.roles', icon: 'shield' },
      { section: 'permissions', labelKey: 'nav.permissions', icon: 'lock' },
      { section: 'assignments', labelKey: 'nav.assignments', icon: 'link' },
      { section: 'audit', labelKey: 'nav.audit', icon: 'list' }
    ]
  },
  {
    labelKey: 'nav.groupConfiguration',
    items: [
      { section: 'model-runners', labelKey: 'nav.modelRunners', icon: 'cpu' },
      { section: 'prompts', labelKey: 'nav.prompts', icon: 'chat' }
    ]
  },
  {
    labelKey: 'nav.groupObservability',
    items: [
      { section: 'requests', labelKey: 'nav.requests', icon: 'chart' },
      { section: 'explorer', labelKey: 'nav.explorer', icon: 'play' }
    ]
  },
  {
    labelKey: 'nav.groupSystem',
    items: [
      { section: 'settings', labelKey: 'nav.settings', icon: 'gear' }
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
  jobs: { titleKey: 'jobs.title', subtitleKey: 'jobs.subtitle' },
  'ingestion-jobs': { titleKey: 'jobs.jobsTitle', subtitleKey: 'jobs.jobsSubtitle' },
  tenants: { titleKey: 'nav.tenants' },
  users: { titleKey: 'nav.users' },
  credentials: { titleKey: 'nav.credentials' },
  roles: { titleKey: 'nav.roles' },
  permissions: { titleKey: 'nav.permissions' },
  assignments: { titleKey: 'nav.assignments' },
  audit: { titleKey: 'nav.audit' },
  'model-runners': { titleKey: 'modelRunners.title', subtitleKey: 'modelRunners.subtitle' },
  prompts: { titleKey: 'prompts.title', subtitleKey: 'prompts.subtitle' },
  requests: { titleKey: 'requests.title', subtitleKey: 'requests.subtitle' },
  explorer: { titleKey: 'explorer.title', subtitleKey: 'explorer.subtitle' },
  settings: { titleKey: 'settings.title', subtitleKey: 'settings.subtitle' }
};
