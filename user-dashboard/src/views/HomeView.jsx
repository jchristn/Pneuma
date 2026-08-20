import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext.jsx';

/**
 * Landing page for the user dashboard: one card per subject. Clicking a card opens that subject's chat
 * at its URL slug. Subjects still being deleted, or without a slug, are not shown as navigable.
 */
export default function HomeView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    apiClient.getSubjects()
      .then((resp) => {
        if (cancelled) return;
        const items = resp?.objects || resp?.items || [];
        setSubjects(items.filter((s) => s.active !== false && (s.deletionStatus == null || s.deletionStatus === 'None')));
      })
      .catch((err) => { if (!cancelled) setError(err?.message || 'Failed to load subjects'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient]);

  return (
    <div className="view home-view">
      <section className="search-hero" style={{ paddingBottom: '1rem' }}>
        <h1 className="hero-title">{t('home.title', 'Explore subjects')}</h1>
        <p className="hero-subtitle">{t('home.subtitle', 'Pick a subject to start a conversation with its knowledge.')}</p>
      </section>

      {error ? <div className="error-banner" role="alert">{error}</div> : null}
      {loading ? (
        <div className="loading-block"><span className="loading-spinner" /> {t('common.loading', 'Loading…')}</div>
      ) : subjects.length === 0 ? (
        <div className="empty-state-title">{t('home.empty', 'No subjects are available yet.')}</div>
      ) : (
        <div className="subject-card-grid">
          {subjects.map((s) => {
            const slug = s.urlSlug || s.id;
            return (
              <Link key={s.id} to={`/${encodeURIComponent(slug)}`} className="subject-card">
                <span className="subject-card-type">{s.type || 'Subject'}</span>
                <span className="subject-card-name">{s.displayName || s.name || slug}</span>
                {s.description ? <span className="subject-card-desc">{s.description}</span> : null}
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
