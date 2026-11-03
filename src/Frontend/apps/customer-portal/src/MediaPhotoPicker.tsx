import {useEffect,useState} from "react";
import {Modal,Typography} from "antd";
import {MediaLibrary,type LibraryPhoto} from "./MediaLibrary";

export function MediaPhotoPicker({open,onCancel,onChoose,selectedId,excludedIds=[],refreshKey=0}:{open:boolean;onCancel:()=>void;onChoose:(photo:LibraryPhoto)=>void;selectedId?:string;excludedIds?:string[];refreshKey?:number}){
 const [candidate,setCandidate]=useState<LibraryPhoto>();
 useEffect(()=>{setCandidate(undefined);},[open,refreshKey]);
 return <Modal title="Choose a photo" open={open} onCancel={onCancel} width={920} styles={{body:{maxHeight:"65vh",overflowY:"auto"}}} destroyOnClose okText="Use selected photo" cancelText="Cancel" okButtonProps={{disabled:!candidate||excludedIds.includes(candidate.id)}} onOk={()=>{if(candidate?.processingStatus==="ready"&&!excludedIds.includes(candidate.id))onChoose(candidate);}}>
  <Typography.Paragraph>Choose a ready photo from your restaurant workspace. This selects a draft; approval and saving happen in the menu or gallery editor.</Typography.Paragraph>
  {open&&<MediaLibrary key={refreshKey} selectedId={candidate?.id??selectedId} excludedIds={excludedIds} onChoose={setCandidate} refreshKey={refreshKey}/>}
  {candidate&&<Typography.Paragraph>Selected: {candidate.originalFileName}</Typography.Paragraph>}
 </Modal>;
}
