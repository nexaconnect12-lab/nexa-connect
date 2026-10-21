import { Button, Drawer, Menu, Tag } from "antd";
import type { MenuProps } from "antd";
import { useRef, useState, type ReactNode } from "react";
import { useAuthorizationUi } from "@nexaconnect/authorization-ui";

export interface NavigationItem {
  key: string;
  label: ReactNode;
  capability?: string;
  onSelect?: () => void;
}

/** Presentation-only frame reusable by later focused views; no routes or grants. */
export function WorkspaceFrame({ title, description, actions, children }: {
  title: string; description?: string; actions?: ReactNode; children: ReactNode;
}) {
  return <section className="nexa-frame" aria-label={title}>
    <div className="nexa-frame-heading"><div><h1>{title}</h1>{description && <p>{description}</p>}</div>{actions}</div>
    <div className="nexa-frame-content">{children}</div>
  </section>;
}

export interface PortalLayoutProps {
  title: string;
  description?: string;
  items: readonly NavigationItem[];
  selectedKey?: string;
  headerActions?: ReactNode;
  children: ReactNode;
}

export function PortalLayout({ title, description, items, selectedKey, headerActions, children }: PortalLayoutProps) {
  const can = useAuthorizationUi();
  const [navigationOpen, setNavigationOpen] = useState(false);
  const main = useRef<HTMLElement>(null);
  const focusContentAfterClose = useRef(false);
  const visible = items.filter(item => !item.capability || can(item.capability));
  const menuItems: MenuProps["items"] = visible.map(item => ({ key: item.key, label: item.label }));
  const selected = visible.find(item => item.key === selectedKey);
  const choose = (key: string) => {
    visible.find(item => item.key === key)?.onSelect?.();
    focusContentAfterClose.current = navigationOpen;
    setNavigationOpen(false);
    if (!navigationOpen) main.current?.focus();
  };
  const navigation = <Menu className="nexa-navigation" aria-label="Workspace sections" mode="inline" selectedKeys={selectedKey ? [selectedKey] : []}
    items={menuItems} onClick={({ key }) => choose(key)} />;
  const brand = <div className="nexa-brand"><div className="nexa-brand-mark">nexa / connect</div><div className="nexa-brand-scope">{title}</div></div>;

  return <div className="nexa-portal">
    <a className="nexa-skip-link" href="#workspace-content" onClick={event => { event.preventDefault(); main.current?.focus(); }}>Skip to workspace</a>
    <aside className="nexa-sidebar">{brand}<nav aria-label="Workspace navigation">{navigation}</nav></aside>
    <div className="nexa-portal-body">
      <header className="nexa-topbar">
        <div><Button className="nexa-nav-trigger" aria-expanded={navigationOpen} onClick={() => setNavigationOpen(true)}>Open navigation</Button>
          <span aria-label="Current section">{selected?.label ?? title}</span></div>
        <div className="nexa-topbar-actions">{headerActions}</div>
      </header>
      <main id="workspace-content" className="nexa-main" tabIndex={-1} ref={main}>
        <WorkspaceFrame title={title} description={description} actions={<Tag className="nexa-session-badge">Authenticated workspace</Tag>}>{children}</WorkspaceFrame>
      </main>
    </div>
    <Drawer title={`${title} navigation`} placement="left" width={320} open={navigationOpen}
      onClose={() => { focusContentAfterClose.current = false; setNavigationOpen(false); }}
      afterOpenChange={open => {
        if (!open && focusContentAfterClose.current) {
          focusContentAfterClose.current = false;
          // Drawer restores the previously active element after this callback. Move
          // selected-page focus only once that restoration has completed.
          queueMicrotask(() => main.current?.focus());
        }
      }}>
      <nav aria-label="Mobile workspace navigation">{navigation}</nav>
    </Drawer>
  </div>;
}
