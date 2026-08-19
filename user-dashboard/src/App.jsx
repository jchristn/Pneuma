import { Navigate, Route, Routes } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from './context/AuthContext.jsx';
import Login from './components/Login.jsx';
import Shell from './components/Shell.jsx';
import SearchView from './views/SearchView.jsx';
import NodeView from './views/NodeView.jsx';
import AskView from './views/AskView.jsx';
import NotFoundView from './views/NotFoundView.jsx';

function FullScreenLoader() {
  const { t } = useTranslation();
  return (
    <div className="app-loading">
      <div className="spinner" />
      <p>{t('common.loading')}</p>
    </div>
  );
}

function ProtectedRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <FullScreenLoader />;
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return children;
}

function PublicRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <FullScreenLoader />;
  if (isAuthenticated) return <Navigate to="/" replace />;
  return children;
}

export default function App() {
  return (
    <Routes>
      <Route
        path="/login"
        element={
          <PublicRoute>
            <Login />
          </PublicRoute>
        }
      />
      <Route
        element={
          <ProtectedRoute>
            <Shell />
          </ProtectedRoute>
        }
      >
        <Route path="/" element={<SearchView />} />
        <Route path="/node/:id" element={<NodeView />} />
        <Route path="/ask" element={<AskView />} />
        <Route path="*" element={<NotFoundView />} />
      </Route>
    </Routes>
  );
}
