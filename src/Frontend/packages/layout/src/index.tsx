import { Layout, Menu, Typography } from "antd";
import type { MenuProps } from "antd";
import type { ReactNode } from "react";
import { useAuthorizationUi } from "@nexaconnect/authorization-ui";

export interface NavigationItem {
  key: string;
  label: ReactNode;
  capability?: string;
  onSelect?: () => void;
}

export interface PortalLayoutProps {
  title: string;
  items: readonly NavigationItem[];
  selectedKey?: string;
  headerActions?: ReactNode;
  children: ReactNode;
}

export function PortalLayout({ title, items, selectedKey, headerActions, children }: PortalLayoutProps) {
  const can = useAuthorizationUi();
  const visible = items.filter(item => !item.capability || can(item.capability));
  const menuItems: MenuProps["items"] = visible.map(item => ({ key: item.key, label: item.label }));
  const callbacks = new Map(visible.map(item => [item.key, item.onSelect]));

  return <Layout style={{ minHeight: "100vh" }}>
    <Layout.Sider breakpoint="lg" collapsedWidth="0" theme="light">
      <Typography.Title level={4} style={{ padding: 20, margin: 0 }}>{title}</Typography.Title>
      <Menu mode="inline" selectedKeys={selectedKey ? [selectedKey] : []} items={menuItems}
        onClick={({ key }) => callbacks.get(key)?.()} />
    </Layout.Sider>
    <Layout>
      <Layout.Header style={{ display: "flex", justifyContent: "flex-end", alignItems: "center", paddingInline: 24 }}>
        {headerActions}
      </Layout.Header>
      <Layout.Content style={{ padding: 24 }}>{children}</Layout.Content>
    </Layout>
  </Layout>;
}
