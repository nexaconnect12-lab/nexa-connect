import {useEffect,useRef} from "react";

// Poll reads only. Callers serialize reads with decisions/submissions and fence late responses.
export function useVisibleRefresh(refresh:()=>Promise<unknown>,interval=15000){
 const current=useRef(refresh);current.current=refresh;
 useEffect(()=>{
  let running=false,disposed=false;
  const tick=async()=>{
   if(disposed||running||document.visibilityState!=="visible"||!document.hasFocus())return;
   running=true;try{await current.current();}finally{running=false;}
  };
  const timer=window.setInterval(()=>void tick(),interval);
  document.addEventListener("visibilitychange",tick);window.addEventListener("focus",tick);
  return()=>{disposed=true;window.clearInterval(timer);document.removeEventListener("visibilitychange",tick);window.removeEventListener("focus",tick);};
 },[interval]);
}
