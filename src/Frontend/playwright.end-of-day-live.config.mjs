import {defineConfig,devices} from '@playwright/test';
import {readSettings} from './e2e/end-of-day-live/settings.mjs';
const settings=readSettings(process.env);
export default defineConfig({testDir:'./e2e/end-of-day-live',testMatch:'*.spec.mjs',workers:1,fullyParallel:false,retries:0,repeatEach:1,
  timeout:180_000,expect:{timeout:20_000},outputDir:`test-results/end-of-day-live/${settings.runId}`,
  reporter:[['./e2e/end-of-day-live/safe-reporter.mjs',{runId:settings.runId}]],
  use:{baseURL:settings.baseURL,ignoreHTTPSErrors:true,trace:'off',screenshot:'off',video:'off',serviceWorkers:'block'},
  projects:[{name:'end-of-day-live',use:{...devices['Desktop Chrome']}}]});
