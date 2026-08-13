import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";
export default defineConfig(({mode})=>{const env=loadEnv(mode,process.cwd(),"");return{plugins:[react()],server:{port:5175,proxy:env.CUSTOMER_BFF_URL?{"/bff":{target:env.CUSTOMER_BFF_URL,secure:false,changeOrigin:true}}:undefined}};});
