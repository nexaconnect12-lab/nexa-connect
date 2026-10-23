import {defineConfig,devices} from "@playwright/test";
export default defineConfig({testDir:"./e2e/gallery",workers:1,retries:0,timeout:30000,outputDir:"test-results/gallery",reporter:"list",
use:{baseURL:"http://127.0.0.1:5198",trace:"off",screenshot:"off",video:"off"},webServer:{command:"npm run dev --workspace @nexaconnect/customer-portal -- --host 127.0.0.1 --port 5198 --strictPort",url:"http://127.0.0.1:5198",reuseExistingServer:false},projects:[{name:"gallery",use:{...devices["Desktop Chrome"]}}]});
