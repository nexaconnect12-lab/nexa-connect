import { defineConfig, devices } from "@playwright/test";
import { createRequire } from "node:module";
import path from "node:path";
const require=createRequire(import.meta.url);
const vite=path.join(path.dirname(require.resolve("vite/package.json")),"bin/vite.js");
export default defineConfig({
  testDir:"./e2e/cash-close",workers:1,timeout:30_000,outputDir:"test-results/cash-close",reporter:"list",
  use:{baseURL:"http://127.0.0.1:5179",trace:"off",screenshot:"off",video:"off"},
  webServer:{command:`"${process.execPath}" "${vite}" apps/customer-portal --host 127.0.0.1 --port 5179 --strictPort`,url:"http://127.0.0.1:5179",reuseExistingServer:false},
  projects:[{name:"cash-close-chromium",use:{...devices["Desktop Chrome"]}}],
});
