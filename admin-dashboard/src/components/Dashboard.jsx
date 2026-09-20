import { useState, useEffect } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { SECTION_META, WORKSPACE_TABS, LEGACY_REDIRECTS } from '../config/nav';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import WorkspaceTabs from './WorkspaceTabs';
import SetupWizard from './SetupWizard';

import HomeView from '../views/HomeView';
import RequestHistoryView from '../views/RequestHistoryView';
import ApiExplorerView from '../views/ApiExplorerView';
import SettingsView from '../views/SettingsView';
import ProcessingView from '../views/ProcessingView';
import IngestionQueueView from '../views/IngestionQueueView';
import IngestionJobsView from '../views/IngestionJobsView';
import IngestionLiveView from '../views/IngestionLiveView';
import NotFound from './NotFound';
import ModelRunnersView from '../views/ModelRunnersView';
import PromptsView from '../views/PromptsView';
import SubjectsView from '../views/SubjectsView';
import LinksView from '../views/LinksView';
import CollectionsView from '../views/CollectionsView';
import SearchView from '../views/SearchView';
import AskView from '../views/AskView';
import ConversationsView from '../views/ConversationsView';
import TenantsView from '../views/TenantsView';
import UsersView from '../views/UsersView';
import CredentialsView from '../views/CredentialsView';
import RolesView from '../views/RolesView';
import PermissionsView from '../views/PermissionsView';
import AssignmentsView from '../views/AssignmentsView';
import AuditView from '../views/AuditView';
import HistoryView from '../views/HistoryView';
import FeedbackView from '../views/FeedbackView';
import AnalyticsView from '../views/AnalyticsView';
import EvalView from '../views/EvalView';

const VIEWS = {
  home: HomeView,
  requests: RequestHistoryView,
  explorer: ApiExplorerView,
  settings: SettingsView,
  processing: ProcessingView,
  'ingestion-live': IngestionLiveView,
  jobs: IngestionQueueView,
  'ingestion-jobs': IngestionJobsView,
  'model-runners': ModelRunnersView,
  prompts: PromptsView,
  subjects: SubjectsView,
  links: LinksView,
  collections: CollectionsView,
  search: SearchView,
  ask: AskView,
  conversations: ConversationsView,
  tenants: TenantsView,
  users: UsersView,
  credentials: CredentialsView,
  roles: RolesView,
  permissions: PermissionsView,
  assignments: AssignmentsView,
  audit: AuditView,
  history: HistoryView,
  analytics: AnalyticsView,
  eval: EvalView,
  feedback: FeedbackView
};

function Dashboard() {
  const { section = 'home', tab } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { t } = useTranslation();
  const { apiClient, authContext } = useAuth();
  const isAdmin = !!authContext?.isAdmin;
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [showWizard, setShowWizard] = useState(false);

  // An old single-view URL (e.g. /dashboard/subjects, /dashboard/jobs) redirects to its consolidated tab home,
  // preserving any query string (e.g. /dashboard/ask?thread=...).
  const legacy = !WORKSPACE_TABS[section] ? LEGACY_REDIRECTS[section] : null;
  useEffect(() => {
    if (legacy) navigate(`/dashboard/${legacy.section}/${legacy.tab}${location.search || ''}`, { replace: true });
  }, [legacy, navigate, location.search]);

  // Resolve the active workspace, its (role-filtered) tabs, and the active tab's view component.
  const allTabs = WORKSPACE_TABS[section] || null;
  const tabs = allTabs ? allTabs.filter((tt) => !tt.adminOnly || isAdmin) : [];
  const activeTab = tabs.find((tt) => tt.key === tab) || tabs[0] || null;
  const ViewComponent = activeTab ? (VIEWS[activeTab.view] || NotFound) : NotFound;
  const showTabs = tabs.length > 1;

  // First run: with no subjects and no model endpoints yet, offer the guided setup wizard (once, unless
  // the operator dismisses it). A reliable local check on subjects anchors the decision.
  useEffect(() => {
    let done = false;
    try { if (localStorage.getItem('pneuma.setupComplete') === '1') return undefined; } catch { /* ignore */ }
    Promise.allSettled([
      apiClient.list('subjects', { maxResults: 1 }),
      apiClient.list('model-runners')
    ]).then((results) => {
      if (done) return;
      const noSubjects = results[0].status === 'fulfilled' && normalizeList(results[0].value).items.length === 0;
      const noRunners = results[1].status !== 'fulfilled' || normalizeList(results[1].value).items.length === 0;
      if (noSubjects && noRunners) setShowWizard(true);
    });
    return () => { done = true; };
  }, [apiClient]);

  useEffect(() => {
    const meta = SECTION_META[section];
    const title = meta ? t(meta.titleKey) : t('app.name');
    document.title = `${title} · ${t('app.name')}`;
  }, [section, t]);

  useEffect(() => { setSidebarOpen(false); }, [section, tab]);

  const handleNavigate = (newSection) => navigate(`/dashboard/${newSection}`);
  const selectTab = (selected) => navigate(`/dashboard/${section}/${selected.key}`);

  // While a legacy URL is redirecting, render nothing to avoid a flash of NotFound.
  if (legacy) return null;

  return (
    <div className="shell">
      <Topbar onToggleSidebar={() => setSidebarOpen((v) => !v)} />
      <Sidebar activeSection={section} onNavigate={handleNavigate} open={sidebarOpen} />
      <main className="workspace">
        {showTabs && <WorkspaceTabs tabs={tabs} activeKey={activeTab?.key} onSelect={selectTab} />}
        <ViewComponent />
      </main>
      {showWizard && <SetupWizard onClose={() => setShowWizard(false)} />}
    </div>
  );
}

export default Dashboard;
