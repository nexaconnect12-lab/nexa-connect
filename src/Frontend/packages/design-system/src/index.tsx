import { App, ConfigProvider, type ThemeConfig } from "antd";
import type { PropsWithChildren } from "react";

export const designTokens = {
  colorPrimary: "#14532d",
  colorInfo: "#0369a1",
  colorSuccess: "#15803d",
  colorWarning: "#b45309",
  colorError: "#b91c1c",
  colorBgLayout: "#f6f7f4",
  borderRadius: 8,
  fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif"
} as const;

export const nexaTheme: ThemeConfig = {
  token: designTokens,
  components: {
    Layout: { bodyBg: designTokens.colorBgLayout, headerBg: "#ffffff", siderBg: "#ffffff" },
    Button: { controlHeight: 40 },
    Input: { controlHeight: 40 }
  }
};

export interface NexaDesignProviderProps extends PropsWithChildren {
  theme?: ThemeConfig;
}

export function NexaDesignProvider({ children, theme = nexaTheme }: NexaDesignProviderProps) {
  return <ConfigProvider theme={theme}><App>{children}</App></ConfigProvider>;
}

export { Alert, Button, Card, Empty, Flex, Space, Spin, Typography } from "antd";
