export interface TabItem {
  id: string;
  label: string;
  disabled?: boolean;
  suffix?: string;
}

interface TabsProps {
  items: TabItem[];
  activeId: string;
}

export function Tabs({ activeId, items }: TabsProps) {
  return (
    <div className="tabs" role="tablist" aria-label="Company profile sections">
      {items.map((tab) => (
        <button
          key={tab.id}
          className={`tab${tab.id === activeId ? " tab--active" : ""}`}
          type="button"
          role="tab"
          aria-selected={tab.id === activeId}
          disabled={tab.disabled}
          title={tab.disabled ? "Available after research is implemented." : undefined}
        >
          {tab.label}
          {tab.suffix ? <> <span className="tab__suffix">{tab.suffix}</span></> : null}
        </button>
      ))}
    </div>
  );
}
