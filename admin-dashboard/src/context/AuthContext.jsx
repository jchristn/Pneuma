import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import ApiClient from '../utils/api';

const AuthContext = createContext(null);

const LS_URL = 'pneuma_admin_server_url';
const LS_TOKEN = 'pneuma_admin_token';
const LS_CTX = 'pneuma_admin_context';
const LS_THEME = 'pneuma_admin_theme';

export const DEFAULT_SERVER_URL = 'http://localhost:8080';

function readJson(key) {
  try {
    const raw = localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [apiClient, setApiClient] = useState(null);
  const [serverUrl, setServerUrl] = useState(DEFAULT_SERVER_URL);
  const [token, setToken] = useState('');
  const [authContext, setAuthContext] = useState(null);
  const [theme, setTheme] = useState('light');

  // Initialize theme + restore session
  useEffect(() => {
    const savedTheme = localStorage.getItem(LS_THEME) || 'light';
    setTheme(savedTheme);
    document.documentElement.setAttribute('data-theme', savedTheme);

    const savedUrl = localStorage.getItem(LS_URL);
    const savedToken = localStorage.getItem(LS_TOKEN);
    const savedCtx = readJson(LS_CTX);

    if (savedUrl && savedToken) {
      const client = new ApiClient(savedUrl, savedToken);
      client.validateToken()
        .then((ctx) => {
          setServerUrl(savedUrl);
          setToken(savedToken);
          setApiClient(client);
          setAuthContext(ctx || savedCtx);
          setIsAuthenticated(true);
        })
        .catch(() => {
          localStorage.removeItem(LS_TOKEN);
          localStorage.removeItem(LS_CTX);
        })
        .finally(() => setIsLoading(false));
    } else {
      setIsLoading(false);
    }
  }, []);

  // React to 401s from anywhere in the app
  useEffect(() => {
    const handler = () => {
      localStorage.removeItem(LS_TOKEN);
      localStorage.removeItem(LS_CTX);
      setIsAuthenticated(false);
      setApiClient(null);
      setToken('');
      setAuthContext(null);
    };
    window.addEventListener('auth:unauthorized', handler);
    return () => window.removeEventListener('auth:unauthorized', handler);
  }, []);

  const login = useCallback(async (url, email, password) => {
    const cleanUrl = (url || '').replace(/\/+$/, '');
    const tempClient = new ApiClient(cleanUrl, null);
    const response = await tempClient.login(email, password);
    const newToken = response.token;
    if (!newToken) throw new Error('No token returned');

    const client = new ApiClient(cleanUrl, newToken);
    const ctx = {
      isAdmin: response.isAdmin,
      isTenantAdmin: response.isTenantAdmin,
      tenantId: response.tenantId,
      userId: response.userId,
      displayName: response.displayName,
      email: response.email,
      principalType: response.principalType,
      expiresUtc: response.expiresUtc
    };

    localStorage.setItem(LS_URL, cleanUrl);
    localStorage.setItem(LS_TOKEN, newToken);
    localStorage.setItem(LS_CTX, JSON.stringify(ctx));

    setServerUrl(cleanUrl);
    setToken(newToken);
    setApiClient(client);
    setAuthContext(ctx);
    setIsAuthenticated(true);
    return ctx;
  }, []);

  const logout = useCallback(async () => {
    try {
      if (apiClient) await apiClient.logout();
    } catch {
      // ignore network errors on logout
    }
    localStorage.removeItem(LS_TOKEN);
    localStorage.removeItem(LS_CTX);
    setIsAuthenticated(false);
    setApiClient(null);
    setToken('');
    setAuthContext(null);
  }, [apiClient]);

  const toggleTheme = useCallback(() => {
    setTheme((prev) => {
      const next = prev === 'light' ? 'dark' : 'light';
      localStorage.setItem(LS_THEME, next);
      document.documentElement.setAttribute('data-theme', next);
      return next;
    });
  }, []);

  const value = {
    isAuthenticated,
    isLoading,
    apiClient,
    serverUrl,
    token,
    authContext,
    theme,
    login,
    logout,
    toggleTheme
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
}

export default AuthContext;
