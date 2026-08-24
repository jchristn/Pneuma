import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

const icon = (path) => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    {path}
  </svg>
);

function Sidebar() {
  const { t } = useTranslation();

  const groups = [
    {
      title: t('nav.overview'),
      items: [
        {
          id: 'home',
          label: t('nav.overview'),
          icon: icon(<><path d="M3 13h8V3H3zM13 21h8V3h-8zM3 21h8v-6H3z" /></>)
        }
      ]
    },
    {
      title: t('nav.content'),
      items: [
        {
          id: 'subjects',
          label: t('nav.subjects'),
          icon: icon(<><circle cx="12" cy="8" r="4" /><path d="M4 21v-1a6 6 0 0 1 12 0v1" /></>)
        },
        {
          id: 'links',
          label: t('nav.links'),
          icon: icon(<><path d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1" /><path d="M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1" /></>)
        },
        {
          id: 'ingestion',
          label: t('nav.ingestion'),
          icon: icon(<><path d="M12 2v13" /><path d="m19 9-7 7-7-7" /><path d="M5 20h14" /></>)
        },
        {
          id: 'ask',
          label: t('nav.ask'),
          icon: icon(<><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" /></>)
        },
        {
          id: 'conversations',
          label: t('nav.conversations', 'Conversations'),
          icon: icon(<><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z" /></>)
        },
        {
          id: 'history',
          label: t('nav.history', 'History'),
          icon: icon(<><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>)
        },
        {
          id: 'analytics',
          label: t('nav.analytics', 'Analytics'),
          icon: icon(<><path d="M3 3v18h18" /><path d="M7 15l3-4 3 3 4-6" /></>)
        },
        {
          id: 'eval',
          label: t('nav.eval', 'Evaluation'),
          icon: icon(<><path d="M9 11l3 3L22 4" /><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" /></>)
        },
        {
          id: 'feedback',
          label: t('nav.feedback', 'Feedback'),
          icon: icon(<><path d="M14 9V5a3 3 0 0 0-6 0v4" /><path d="M5 9h14l1 11H4z" /></>)
        }
      ]
    },
    {
      title: t('nav.observability'),
      items: [
        {
          id: 'requests',
          label: t('nav.requestHistory'),
          icon: icon(<><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>)
        },
        {
          id: 'explorer',
          label: t('nav.apiExplorer'),
          icon: icon(<><polygon points="5 3 19 12 5 21 5 3" /></>)
        }
      ]
    },
    {
      title: t('nav.settings'),
      items: [
        {
          id: 'settings',
          label: t('nav.settings'),
          icon: icon(<><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" /></>)
        }
      ]
    }
  ];

  return (
    <aside className="sidebar">
      <nav className="sidebar-nav" aria-label="Primary">
        {groups.map((group) => (
          <div className="nav-section" key={group.title}>
            <div className="nav-section-title">{group.title}</div>
            {group.items.map((item) => (
              <NavLink
                key={item.id}
                to={`/dashboard/${item.id}`}
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
                title={item.label}
              >
                <span className="nav-icon">{item.icon}</span>
                <span className="nav-label">{item.label}</span>
              </NavLink>
            ))}
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        <div>Pneuma Subject Dashboard</div>
        <div>v1.0.0</div>
      </div>
    </aside>
  );
}

export default Sidebar;
