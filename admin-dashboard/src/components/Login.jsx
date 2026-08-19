import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth, DEFAULT_SERVER_URL } from '../context/AuthContext';
import LanguageSelector from '../i18n/LanguageSelector';
import logo from '../assets/logo.png';

function Login() {
  const { t } = useTranslation();
  const { login, theme, toggleTheme } = useAuth();
  const [serverUrl, setServerUrl] = useState(DEFAULT_SERVER_URL);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(serverUrl, email, password);
    } catch (err) {
      setError(err?.message ? `${t('login.failed')} (${err.message})` : t('login.failed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-top">
          <LanguageSelector />
          <button type="button" className="icon-button" onClick={toggleTheme} aria-label={t('settings.theme')}>
            {theme === 'light' ? '🌙' : '☀️'}
          </button>
        </div>
        <div className="login-brand">
          <img src={logo} alt="Pneuma" />
          <h1>{t('login.title')}</h1>
          <p>{t('login.subtitle')}</p>
        </div>
        <form className="login-form" onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="serverUrl">{t('login.serverUrl')}</label>
            <input id="serverUrl" type="text" value={serverUrl}
              onChange={(e) => setServerUrl(e.target.value)}
              placeholder={DEFAULT_SERVER_URL} required disabled={loading} />
          </div>
          <div className="field">
            <label htmlFor="email">{t('login.email')}</label>
            <input id="email" type="email" value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="admin@pneuma" required disabled={loading} autoComplete="username" />
          </div>
          <div className="field">
            <label htmlFor="password">{t('login.password')}</label>
            <input id="password" type="password" value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••" required disabled={loading} autoComplete="current-password" />
          </div>
          {error && <div className="error-message">{error}</div>}
          <button type="submit" className="button-primary" disabled={loading}>
            {loading ? t('login.connecting') : t('login.connect')}
          </button>
        </form>
        <div className="login-hint">{t('login.hint')}</div>
      </div>
    </div>
  );
}

export default Login;
