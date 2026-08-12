import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  return {
    plugins: [react()],
    server: {
      port: 5174,
      proxy: env.PRODUCT_OWNER_BFF_URL ? { "/bff": { target: env.PRODUCT_OWNER_BFF_URL, secure: false, changeOrigin: true } } : undefined
    }
  };
});
