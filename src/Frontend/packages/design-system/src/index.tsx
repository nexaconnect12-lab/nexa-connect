import { Alert, App, Button, ConfigProvider, Empty, Spin, type ThemeConfig } from "antd";
import type { PropsWithChildren } from "react";

export const designTokens = {
  colorPrimary: "#14532d",
  colorInfo: "#0369a1",
  colorSuccess: "#15803d",
  colorWarning: "#b45309",
  colorError: "#b91c1c",
  colorBgLayout: "#f4f6f3",
  colorText: "#172a22",
  colorTextSecondary: "#52655a",
  colorBorder: "#d3ddd6",
  fontSize: 14,
  controlHeight: 44,
  borderRadius: 8,
  fontFamily: '"Segoe UI", ui-sans-serif, system-ui, sans-serif'
} as const;

export const nexaTheme: ThemeConfig = {
  token: designTokens,
  components: {
    Layout: { bodyBg: designTokens.colorBgLayout, headerBg: "#ffffff", siderBg: "#ffffff" },
    Button: { controlHeight: 44, fontWeight: 600 },
    Input: { controlHeight: 44 },
    Menu: { itemHeight: 48, itemSelectedBg: "#e6ede7", itemSelectedColor: "#14532d" },
    Table: { headerBg: "#f4f6f3", rowHoverBg: "#f6f9f6" },
    Card: { headerFontSize: 18 }
  }
};

export interface NexaDesignProviderProps extends PropsWithChildren {
  theme?: ThemeConfig;
}

export function NexaDesignProvider({ children, theme = nexaTheme }: NexaDesignProviderProps) {
  return <ConfigProvider theme={theme}><style>{foundationStyles}</style><App className="nexa-app">{children}</App></ConfigProvider>;
}

/** Read-only request feedback. Retry is supplied by the owning query, never a mutation. */
export function RequestState({ state, title, message, onRetry }: {
  state: "loading" | "empty" | "error"; title?: string; message?: string; onRetry?: () => void;
}) {
  return <section className="nexa-request-state" aria-label={title ?? (state === "loading" ? "Loading workspace" : "Request status")}>
    {state === "loading" ? <div role="status" aria-live="polite"><Spin /><p>{title ?? "Loading workspace"}</p><span>{message ?? "Please wait while we load your authorized workspace."}</span></div>
      : state === "empty" ? <Empty description={message ?? "There is nothing to display yet."} />
      : <Alert type="error" showIcon message={title ?? "Unable to load this view"} description={message}
          action={onRetry ? <Button onClick={onRetry}>Try again</Button> : undefined} />}
  </section>;
}

const foundationStyles = `
html, body, #root { margin: 0; min-height: 100%; }
body { background: #f4f6f3; }
*, *::before, *::after { box-sizing: border-box; }
.nexa-app { min-height: 100vh; color: #172a22; font-variant-numeric: tabular-nums; }
.nexa-app :focus-visible { outline: 3px solid #0369a1; outline-offset: 3px; }
.nexa-app .ant-table-wrapper { max-width: 100%; overflow-x: auto; }
.nexa-app .ant-table-cell { overflow-wrap: anywhere; }
.nexa-app .ant-descriptions-item-content { overflow-wrap: anywhere; }
.nexa-app .ant-descriptions-view { overflow-x: auto; }
.nexa-app .ant-card { box-shadow: 0 2px 8px #172a2205; }
.nexa-app .ant-form-item-label > label { font-weight: 600; }
.nexa-app .ant-alert { margin-block: 8px; }
.nexa-request-state { margin: 24px auto; padding: 32px; max-width: 760px; min-width: 0; }
.nexa-request-state [role=status] { text-align: center; padding-block: 48px; color: #52655a; }
.nexa-request-state [role=status] p { color: #172a22; font-size: 20px; font-weight: 600; }
.nexa-request-state .ant-alert { align-items: flex-start; flex-wrap: wrap; gap: 12px; }
.nexa-portal { display: grid; grid-template-columns: 256px minmax(0, 1fr); min-height: 100vh; }
.nexa-sidebar { background: #fff; border-right: 1px solid #dfe6df; padding: 28px 12px; }
.nexa-brand { padding: 0 16px 24px; }
.nexa-brand-mark { color: #14532d; font-size: 22px; font-weight: 750; letter-spacing: -0.6px; }
.nexa-brand-scope { color: #52655a; font-size: 12px; margin-top: 6px; text-transform: uppercase; letter-spacing: 1.2px; }
.nexa-navigation .ant-menu-title-content { white-space: normal; line-height: 20px; text-overflow: clip; }
.nexa-portal-body { min-width: 0; }
.nexa-topbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; padding: 20px 32px; background: #fff; border-bottom: 1px solid #dfe6df; min-height: 84px; }
.nexa-topbar-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; min-width: 0; max-width: 100%; }
.nexa-topbar-actions > .ant-space { max-width: 100%; }
.nexa-topbar-actions .ant-space-item { min-width: 0; max-width: 100%; overflow-wrap: anywhere; }
.nexa-context-picker { width: 320px; max-width: 100%; }
.nexa-nav-trigger { display: none; margin-inline-end: 12px; }
.nexa-session-badge { color: #14532d; background: #e6ede7; border-color: #d3ddd6; }
.nexa-main { padding: 32px; min-width: 0; max-width: 1640px; margin-inline: auto; }
.nexa-frame { min-width: 0; }
.nexa-frame-heading { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 16px; align-items: center; margin-bottom: 24px; }
.nexa-frame-heading h1 { margin: 0; font-size: clamp(24px, 2.4vw, 32px); letter-spacing: -0.6px; }
.nexa-frame-heading p { color: #52655a; margin: 8px 0 0; max-width: 70ch; line-height: 1.6; }
.nexa-frame-content { min-width: 0; }
.nexa-skip-link { position: fixed; left: 12px; top: -80px; z-index: 2000; padding: 12px 20px; background: #14532d; color: #fff; border-radius: 8px; }
.nexa-skip-link:focus { top: 12px; color: #fff; }
@media (max-width: 991px) {
 .nexa-portal { grid-template-columns: minmax(0, 1fr); }
 .nexa-sidebar { display: none; }
 .nexa-nav-trigger { display: inline-flex; }
 .nexa-topbar { padding: 16px; }
 .nexa-topbar-actions { width: 100%; }
 .nexa-main { padding: 24px 16px; }
 .nexa-request-state { padding: 20px; margin: 12px; }
}
@media (prefers-reduced-motion: reduce) {
 .nexa-app *, .nexa-app *::before, .nexa-app *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; }
}
`;

export { Alert, Button, Card, Empty, Flex, Space, Spin, Typography } from "antd";
