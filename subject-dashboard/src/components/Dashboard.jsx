import { useParams } from 'react-router-dom';
import Topbar from './Topbar';
import Sidebar from './Sidebar';
import HomeView from '../views/HomeView';
import SubjectsView from '../views/SubjectsView';
import LinksView from '../views/LinksView';
import IngestionView from '../views/IngestionView';
import AskView from '../views/AskView';
import RequestHistoryView from '../views/RequestHistoryView';
import ApiExplorerView from '../views/ApiExplorerView';
import SettingsView from '../views/SettingsView';
import NotFound from './NotFound';
import './Dashboard.css';

function Dashboard() {
  const { section = 'home' } = useParams();

  const renderView = () => {
    switch (section) {
      case 'home':
        return <HomeView />;
      case 'subjects':
        return <SubjectsView />;
      case 'links':
        return <LinksView />;
      case 'ingestion':
        return <IngestionView />;
      case 'ask':
        return <AskView />;
      case 'requests':
        return <RequestHistoryView />;
      case 'explorer':
        return <ApiExplorerView />;
      case 'settings':
        return <SettingsView />;
      default:
        return <NotFound />;
    }
  };

  return (
    <div className="dashboard">
      <Topbar />
      <div className="dashboard-body">
        <Sidebar />
        <main className="dashboard-main">{renderView()}</main>
      </div>
    </div>
  );
}

export default Dashboard;
