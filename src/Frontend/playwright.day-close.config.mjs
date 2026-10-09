import {defineConfig,devices} from "@playwright/test";
export default defineConfig({testDir:"./e2e/day-close",workers:1,timeout:30_000,outputDir:"test-results/day-close",reporter:"list",use:{baseURL:"http://127.0.0.1:5181",trace:"off",screenshot:"off",video:"off"},globalSetup:"./e2e/financial-completeness/server.mjs",projects:[{name:"day-close-chromium",use:{...devices["Desktop Chrome"]}}]});
