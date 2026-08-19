import { useTranslation } from 'react-i18next';
import { NAV_GROUPS } from '../config/nav';
import Icon from './Icon';

function Sidebar({ activeSection, onNavigate, open }) {
  const { t } = useTranslation();
  return (
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <nav className="sidebar-nav" aria-label="Primary">
        {NAV_GROUPS.map((group) => (
          <div className="nav-group" key={group.labelKey}>
            <div className="nav-group-label">{t(group.labelKey)}</div>
            {group.items.map((item) => (
              <button
                key={item.section}
                type="button"
                className={`nav-item ${activeSection === item.section ? 'active' : ''}`}
                onClick={() => onNavigate(item.section)}
                aria-current={activeSection === item.section ? 'page' : undefined}
              >
                <span className="nav-icon"><Icon name={item.icon} /></span>
                <span>{t(item.labelKey)}</span>
              </button>
            ))}
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        Pneuma Admin · v1.0.0
      </div>
    </aside>
  );
}

export default Sidebar;
