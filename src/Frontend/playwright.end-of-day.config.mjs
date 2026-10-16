import {defineConfig,devices} from "@playwright/test";
export default defineConfig({
  testDir:"./e2e/end-of-day",workers:1,timeout:30_000,outputDir:"test-results/end-of-day",reporter:"list",
  use:{baseURL:"http://127.0.0.1:5181",trace:"off",screenshot:"off",video:"off"},
  globalSetup:"./e2e/financial-completeness/server.mjs",
  projects:[{name:"end-of-day-chromium",use:{...devices["Desktop Chrome"]}}],
});
