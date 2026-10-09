import {createServer} from "vite";
import {fileURLToPath} from "node:url";

// Keep the test-owned server in the runner process so teardown does not depend on Windows process-tree termination.
export default async function setup(){
  const server=await createServer({root:fileURLToPath(new URL("../../apps/customer-portal",import.meta.url)),
    server:{host:"127.0.0.1",port:5181,strictPort:true}});
  try{await server.listen();}catch(error){await server.close();throw error;}
  return async()=>await server.close();
}
