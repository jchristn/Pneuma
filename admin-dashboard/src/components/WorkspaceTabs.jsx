import { useTranslation } from 'react-i18next';

// The tab strip at the top of a consolidated workspace page. Each tab is a formerly-standalone view; selecting
// one deep-links to /dashboard/{workspace}/{tab}. Tabs the caller filtered out (e.g. admin-only) are simply absent.
function WorkspaceTabs({ tabs, activeKey, onSelect }) {
  const { t } = useTranslation();
  return (
    <div className="workspace-tabs" role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          role="tab"
          aria-selected={tab.key === activeKey}
          className={`workspace-tab ${tab.key === activeKey ? 'active' : ''}`}
          onClick={() => onSelect(tab)}
        >
          {t(tab.labelKey)}
        </button>
      ))}
    </div>
  );
}

export default WorkspaceTabs;
