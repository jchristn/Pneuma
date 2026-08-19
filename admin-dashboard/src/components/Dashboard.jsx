import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { SECTION_META } from '../config/nav';
import Sidebar from './Sidebar';
import Topbar from './Topbar';

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
  const [sidebarOpen, setSidebarOpen] = useState(false);

  const ViewComponent = VIEWS[section] || NotFound;

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
    </div>
  );
}

export default Dashboard;
