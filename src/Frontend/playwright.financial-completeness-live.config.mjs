import {defineConfig,devices} from '@playwright/test';
import {readSettings} from './e2e/financial-completeness-live/settings.mjs';
const settings=readSettings(process.env);
export default defineConfig({testDir:'./e2e/financial-completeness-live',testMatch:'*.spec.mjs',workers:1,fullyParallel:false,retries:0,repeatEach:1,
  timeout:180_000,expect:{timeout:20_000},outputDir:`test-results/financial-completeness-live/${settings.runId}`,
  reporter:[['./e2e/financial-completeness-live/safe-reporter.mjs',{runId:settings.runId}]],
  use:{baseURL:settings.baseURL,ignoreHTTPSErrors:true,trace:'off',screenshot:'off',video:'off',serviceWorkers:'block'},
  projects:[{name:'financial-completeness-live',use:{...devices['Desktop Chrome']}}]});
