import {useEffect,useRef} from "react";
import type {BranchMenuOrganization} from "./menuOrganization";

export type ManagedMenuItem={productId:string;name:string;unitPrice:number;currency:string;preparationStation:string;available:boolean;version:number};
export type MenuPanelState={dirty:boolean;busy:boolean};
export type MenuManagementScope={
 organizationId:string;branch:string;product:string;items:ManagedMenuItem[];
 onState:(state:MenuPanelState)=>void;
 onDetailsSaved:(item:ManagedMenuItem)=>void;
 onAvailabilitySaved:(product:string,available:boolean)=>void;
 onOrganizationSaved:(layout:BranchMenuOrganization)=>void;
 onImageSaved:()=>void;
 onInvalidated:()=>void;
};
export function useMenuPanelState(scope:MenuManagementScope|undefined,dirty:boolean,busy:boolean){
 const latest=useRef({dirty,busy});latest.current={dirty,busy};
 useEffect(()=>{scope?.onState({dirty,busy});},[scope?.onState,dirty,busy]);
 useEffect(()=>()=>scope?.onState({dirty:false,busy:false}),[scope?.onState]);
 return {
  begin:()=>scope?.onState({...latest.current,busy:true}),
  end:()=>scope?.onState({...latest.current,busy:false})
 };
}
