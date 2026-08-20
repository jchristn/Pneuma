import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { SECTION_META } from '../config/nav';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import SetupWizard from './SetupWizard';

import HomeView from '../views/HomeView';
import RequestHistoryView from '../views/RequestHistoryView';
import ApiExplorerView from '../views/ApiExplorerView';
import SettingsView from '../views/SettingsView';
import IngestionQueueView from '../views/IngestionQueueView';
import IngestionJobsView from '../views/IngestionJobsView';
import NotFound from './NotFound';
import ModelRunnersView from '../views/ModelRunnersView';
import PromptsView from '../views/PromptsView';
import SubjectsView from '../views/SubjectsView';
import LinksView from '../views/LinksView';
import CollectionsView from '../views/CollectionsView';
import SearchView from '../views/SearchView';
import AskView from '../views/AskView';
import TenantsView from '../views/TenantsView';
import UsersView from '../views/UsersView';
import CredentialsView from '../views/CredentialsView';
import RolesView from '../views/RolesView';
import PermissionsView from '../views/PermissionsView';
import AssignmentsView from '../views/AssignmentsView';
import AuditView from '../views/AuditView';

const VIEWS = {
  home: HomeView,
  requests: RequestHistoryView,
  explorer: ApiExplorerView,
  settings: SettingsView,
  jobs: IngestionQueueView,
  'ingestion-jobs': IngestionJobsView,
  'model-runners': ModelRunnersView,
  prompts: PromptsView,
  subjects: SubjectsView,
  links: LinksView,
  collections: CollectionsView,
  search: SearchView,
  ask: AskView,
  tenants: TenantsView,
  users: UsersView,
  credentials: CredentialsView,
  roles: RolesView,
  permissions: PermissionsView,
  assignments: AssignmentsView,
  audit: AuditView
};

function Dashboard() {
  const { section = 'home' } = useParams();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [showWizard, setShowWizard] = useState(false);

  const ViewComponent = VIEWS[section] || NotFound;

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

  useEffect(() => { setSidebarOpen(false); }, [section]);

  const handleNavigate = (newSection) => navigate(`/dashboard/${newSection}`);

  return (
    <div className="shell">
      <Topbar onToggleSidebar={() => setSidebarOpen((v) => !v)} />
      <Sidebar activeSection={section} onNavigate={handleNavigate} open={sidebarOpen} />
      <main className="workspace">
        <ViewComponent />
      </main>
      {showWizard && <SetupWizard onClose={() => setShowWizard(false)} />}
    </div>
  );
}

export default Dashboard;
