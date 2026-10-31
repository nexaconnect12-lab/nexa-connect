import {useEffect,useRef,useState} from "react";

// Same-origin processed image only. Never accept object-storage URLs or guest capabilities here.
export function MenuPhoto({src,name,refreshKey=0,onReady}:{src:string;name:string;refreshKey?:number;onReady?:(ready:boolean)=>void}){
 const [epoch,setEpoch]=useState(0),[failed,setFailed]=useState(false),[visible,setVisible]=useState(!document.hidden);
 const callback=useRef(onReady);callback.current=onReady;
 useEffect(()=>{setFailed(false);setEpoch(v=>v+1);callback.current?.(false);},[src,refreshKey]);
 useEffect(()=>{const refresh=()=>{callback.current?.(false);setFailed(false);setEpoch(v=>v+1);setVisible(!document.hidden);};
  const timer=setInterval(refresh,60_000);document.addEventListener("visibilitychange",refresh);
  return()=>{clearInterval(timer);document.removeEventListener("visibilitychange",refresh);};},[]);
 if(!visible||failed||!src.startsWith("/bff/")||src.includes("//"))return <span aria-label={`No image for ${name}`} style={{color:"#64748b"}}>No image</span>;
 return <img key={`${src}:${epoch}:${refreshKey}`} src={src} alt={name} loading="lazy" width={96} height={80}
  style={{objectFit:"contain",borderRadius:8,background:"#f1f5f9"}} onLoad={()=>callback.current?.(true)} onError={()=>{setFailed(true);callback.current?.(false);}}/>;
}
