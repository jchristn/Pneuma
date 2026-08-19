import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import ApiClient from '../utils/api.js';

const AuthContext = createContext(null);

const DEFAULT_ENDPOINT = 'http://localhost:8080';
const KEYS = {
  endpoint: 'pneuma.endpoint',
  token: 'pneuma.token',
  user: 'pneuma.user'
};

export function AuthProvider({ children }) {
  const [endpoint, setEndpoint] = useState(() => localStorage.getItem(KEYS.endpoint) || DEFAULT_ENDPOINT);
  const [token, setToken] = useState(() => localStorage.getItem(KEYS.token) || null);
  const [user, setUser] = useState(() => {
    const raw = localStorage.getItem(KEYS.user);
    return raw ? JSON.parse(raw) : null;
  });
  // Start in a loading state only when we have a token to validate.
  const [isLoading, setIsLoading] = useState(() => Boolean(localStorage.getItem(KEYS.token)));

  // A memoized client so views can call apiClient.search(...) etc.
  const apiClient = useMemo(() => new ApiClient(endpoint, token), [endpoint, token]);

  const logout = useCallback(() => {
    // Best-effort server-side revoke; ignore failures.
    if (token) {
      new ApiClient(endpoint, token).revokeToken().catch(() => {});
    }
    localStorage.removeItem(KEYS.token);
    localStorage.removeItem(KEYS.user);
    setToken(null);
    setUser(null);
    setIsLoading(false);
  }, [endpoint, token]);

  // Validate a restored token on mount.
  useEffect(() => {
    if (!token) {
      setIsLoading(false);
      return;
    }
    let cancelled = false;
    setIsLoading(true);
    apiClient
      .validateToken()
      .then(() => {
        if (!cancelled) setIsLoading(false);
      })
      .catch(() => {
        if (!cancelled) {
          localStorage.removeItem(KEYS.token);
          localStorage.removeItem(KEYS.user);
          setToken(null);
          setUser(null);
          setIsLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
    // Only run for the initial restored token.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Global 401 handler dispatched by the API client.
  useEffect(() => {
    const handler = () => logout();
    window.addEventListener('pneuma:unauthorized', handler);
    return () => window.removeEventListener('pneuma:unauthorized', handler);
  }, [logout]);

  const login = useCallback(async (url, email, password) => {
    const cleanUrl = (url || DEFAULT_ENDPOINT).trim().replace(/\/+$/, '');
    const client = new ApiClient(cleanUrl, null);
    const result = await client.login(email, password);
    const nextToken = result?.token;
    if (!nextToken) throw new Error('No token returned by server');

    const nextUser = {
      email: result.email || email,
      displayName: result.displayName || email,
      userId: result.userId || null,
      tenantId: result.tenantId || null,
      isAdmin: Boolean(result.isAdmin),
      isTenantAdmin: Boolean(result.isTenantAdmin)
    };

    localStorage.setItem(KEYS.endpoint, cleanUrl);
    localStorage.setItem(KEYS.token, nextToken);
    localStorage.setItem(KEYS.user, JSON.stringify(nextUser));

    setEndpoint(cleanUrl);
    setToken(nextToken);
    setUser(nextUser);
    setIsLoading(false);
  }, []);

  const value = useMemo(
    () => ({
      endpoint,
      token,
      user,
      apiClient,
      isAuthenticated: Boolean(token),
      isLoading,
      login,
      logout,
      setEndpoint
    }),
    [endpoint, token, user, apiClient, isLoading, login, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside an AuthProvider');
  return context;
}

export default AuthContext;
