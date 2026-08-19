import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import ThemeToggle from './ThemeToggle.jsx';
import LanguageSelector from './LanguageSelector.jsx';
import Icon from './Icon.jsx';

export default function Login() {
  const { t } = useTranslation();
  const { login, endpoint } = useAuth();
  const [serverUrl, setServerUrl] = useState(endpoint || 'http://localhost:8080');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(serverUrl, email, password);
    } catch (err) {
      setError(err?.message ? `${t('login.failed')}` : t('login.failed'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-page">
      <div className="login-utilities">
        <LanguageSelector />
        <ThemeToggle />
      </div>

      <div className="login-panel">
        <div className="login-brand">
          <img src="/logo.png" alt="Pneuma — Pneuma - information brought to life" className="login-logo" />
        </div>

        <h1 className="login-title">{t('login.title')}</h1>
        <p className="login-subtitle">{t('login.subtitle')}</p>

        <form className="login-form" onSubmit={handleSubmit}>
          <div className="form-field">
            <label htmlFor="serverUrl">{t('login.serverUrl')}</label>
            <input
              id="serverUrl"
              type="text"
              value={serverUrl}
              onChange={(e) => setServerUrl(e.target.value)}
              placeholder={t('login.serverUrlPlaceholder')}
              autoComplete="url"
              required
              disabled={loading}
            />
          </div>

          <div className="form-field">
            <label htmlFor="email">{t('login.email')}</label>
            <input
              id="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder={t('login.emailPlaceholder')}
              autoComplete="username"
              required
              disabled={loading}
            />
          </div>

          <div className="form-field">
            <label htmlFor="password">{t('login.password')}</label>
            <div className="password-field">
              <input
                id="password"
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={t('login.passwordPlaceholder')}
                autoComplete="current-password"
                required
                disabled={loading}
              />
              <button
                type="button"
                className="icon-button password-reveal"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? t('login.hidePassword') : t('login.showPassword')}
                title={showPassword ? t('login.hidePassword') : t('login.showPassword')}
                tabIndex={-1}
              >
                <Icon name={showPassword ? 'eyeOff' : 'eye'} size={16} />
              </button>
            </div>
          </div>

          {error ? (
            <div className="form-error" role="alert">
              <Icon name="alert" size={16} />
              <span>{error}</span>
            </div>
          ) : null}

          <button type="submit" className="button button-primary login-submit" disabled={loading}>
            {loading ? t('login.submitting') : t('login.submit')}
          </button>
        </form>

        <p className="login-hint">{t('login.hint')}</p>
      </div>
    </div>
  );
}
