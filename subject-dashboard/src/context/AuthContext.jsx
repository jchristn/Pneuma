import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import ApiClient from '../utils/api';

const AuthContext = createContext(null);

const LS_URL = 'pneuma_subject_server_url';
const LS_TOKEN = 'pneuma_subject_token';
const LS_THEME = 'pneuma_subject_theme';
const LS_USER = 'pneuma_subject_user';

/**
 * Build a normalized principal object from a token/validate response.
 */
function toPrincipal(data) {
  if (!data || typeof data !== 'object') return null;
  return {
    email: data.email || '',
    displayName: data.displayName || data.email || '',
    tenantId: data.tenantId || '',
    userId: data.userId || '',
    principalType: data.principalType || '',
    isAdmin: Boolean(data.isAdmin),
    isTenantAdmin: Boolean(data.isTenantAdmin)
  };
}

export function AuthProvider({ children }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [apiClient, setApiClient] = useState(null);
  const [serverUrl, setServerUrl] = useState('');
  const [token, setToken] = useState('');
  const [principal, setPrincipal] = useState(null);
  const [theme, setTheme] = useState('light');

  // Restore session from localStorage.
  useEffect(() => {
    const savedUrl = localStorage.getItem(LS_URL);
    const savedToken = localStorage.getItem(LS_TOKEN);
    const savedTheme = localStorage.getItem(LS_THEME) || 'light';
    const savedUser = localStorage.getItem(LS_USER);

    setTheme(savedTheme);
    document.documentElement.setAttribute('data-theme', savedTheme);
    document.body.setAttribute('data-theme', savedTheme);

    if (savedUrl && savedToken) {
      const client = new ApiClient(savedUrl, savedToken);
      client
        .validateToken()
        .then((response) => {
          setServerUrl(savedUrl);
          setToken(savedToken);
          setApiClient(client);
          setIsAuthenticated(true);
          const p = toPrincipal(response) || (savedUser ? JSON.parse(savedUser) : null);
          setPrincipal(p);
          if (p) localStorage.setItem(LS_USER, JSON.stringify(p));
        })
        .catch(() => {
          localStorage.removeItem(LS_URL);
          localStorage.removeItem(LS_TOKEN);
          localStorage.removeItem(LS_USER);
        })
        .finally(() => setIsLoading(false));
    } else {
      setIsLoading(false);
    }
  }, []);

  // Global 401 handling.
  useEffect(() => {
    const handler = () => {
      localStorage.removeItem(LS_URL);
      localStorage.removeItem(LS_TOKEN);
      localStorage.removeItem(LS_USER);
      setServerUrl('');
      setToken('');
      setApiClient(null);
      setPrincipal(null);
      setIsAuthenticated(false);
    };
    window.addEventListener('auth:unauthorized', handler);
    return () => window.removeEventListener('auth:unauthorized', handler);
  }, []);

  const login = useCallback(async (url, email, password) => {
    const tempClient = new ApiClient(url, null);
    const response = await tempClient.login(email, password);
    const finalToken = response && response.token;
    if (!finalToken) {
      throw new Error('Login response did not include a token.');
    }

    const client = new ApiClient(url, finalToken);
    const principalData = toPrincipal(response);

    localStorage.setItem(LS_URL, url);
    localStorage.setItem(LS_TOKEN, finalToken);
    if (principalData) localStorage.setItem(LS_USER, JSON.stringify(principalData));

    setServerUrl(url);
    setToken(finalToken);
    setApiClient(client);
    setPrincipal(principalData);
    setIsAuthenticated(true);
    return principalData;
  }, []);

  const logout = useCallback(async () => {
    try {
      if (apiClient) await apiClient.logout();
    } catch {
      // best-effort revoke; clear local state regardless
    }
    localStorage.removeItem(LS_URL);
    localStorage.removeItem(LS_TOKEN);
    localStorage.removeItem(LS_USER);
    setServerUrl('');
    setToken('');
    setApiClient(null);
    setPrincipal(null);
    setIsAuthenticated(false);
  }, [apiClient]);

  const toggleTheme = useCallback(() => {
    setTheme((prev) => {
      const next = prev === 'light' ? 'dark' : 'light';
      localStorage.setItem(LS_THEME, next);
      document.documentElement.setAttribute('data-theme', next);
      document.body.setAttribute('data-theme', next);
      return next;
    });
  }, []);

  const value = {
    isAuthenticated,
    isLoading,
    apiClient,
    serverUrl,
    token,
    principal,
    theme,
    login,
    logout,
    toggleTheme
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

export default AuthContext;
