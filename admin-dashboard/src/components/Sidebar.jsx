import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { NAV_GROUPS } from '../config/nav';
import { useAuth } from '../context/AuthContext';
import Icon from './Icon';

const COLLAPSED_KEY = 'pneuma.nav.collapsedGroups';

function loadCollapsed() {
  try { return JSON.parse(localStorage.getItem(COLLAPSED_KEY)) || {}; } catch { return {}; }
}

// A small chevron that rotates via CSS when its group is collapsed.
function Chevron() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="6 9 12 15 18 9" />
    </svg>
  );
}

function Sidebar({ activeSection, onNavigate, open }) {
  const { t } = useTranslation();
  const { authContext } = useAuth();
  const isAdmin = !!authContext?.isAdmin;
  // Per-group collapsed state, persisted so the operator's preference survives navigation and reloads.
  const [collapsed, setCollapsed] = useState(loadCollapsed);

  const toggleGroup = useCallback((key) => {
    setCollapsed((prev) => {
      const next = { ...prev, [key]: !prev[key] };
      try { localStorage.setItem(COLLAPSED_KEY, JSON.stringify(next)); } catch { /* ignore */ }
      return next;
    });
  }, []);

  // Admin-only nav items (e.g. system-wide ingestion tuning) are hidden from non-system-admins; the
  // backend enforces the same restriction with a 403.
  const visibleGroups = NAV_GROUPS
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.adminOnly || isAdmin) }))
    .filter((group) => group.items.length > 0);

  return (
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <nav className="sidebar-nav" aria-label="Primary">
        {visibleGroups.map((group) => {
          const isCollapsed = !!collapsed[group.labelKey];
          const groupActive = group.items.some((item) => item.section === activeSection);
          return (
            <div className={`nav-group ${isCollapsed ? 'collapsed' : ''} ${groupActive ? 'active' : ''}`} key={group.labelKey}>
              <button
                type="button"
                className="nav-group-label"
                onClick={() => toggleGroup(group.labelKey)}
                aria-expanded={!isCollapsed}
              >
                <span>{t(group.labelKey)}</span>
                <span className="nav-group-chevron"><Chevron /></span>
              </button>
              {!isCollapsed && group.items.map((item) => (
                <button
                  key={item.section}
                  type="button"
                  className={`nav-item ${activeSection === item.section ? 'active' : ''}`}
                  onClick={() => onNavigate(item.section)}
                  aria-current={activeSection === item.section ? 'page' : undefined}
                  title={item.tip}
                >
                  <span className="nav-icon"><Icon name={item.icon} /></span>
                  <span>{t(item.labelKey)}</span>
                </button>
              ))}
            </div>
          );
        })}
      </nav>
      <div className="sidebar-footer">
        Pneuma Admin · v1.0.0
      </div>
    </aside>
  );
}

export default Sidebar;
