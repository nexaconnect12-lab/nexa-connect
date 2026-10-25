import {afterEach,describe,expect,it,vi} from "vitest";
import {createRoot,Root} from "react-dom/client";
import {act} from "react-dom/test-utils";
import {qrStatus} from "./qrStatus";
import {useVisibleRefresh} from "./useVisibleRefresh";

describe("QR presentation",()=>{
 it.each([
  [{status:"pending"},"Waiting for staff"],
  [{status:"price_review"},"Review updated prices"],
  [{status:"accepted",collectionStatus:"preparing"},"Preparing"],
  [{status:"accepted",collectionStatus:"awaiting_cashier"},"Awaiting cashier"],
  [{status:"accepted",collectionStatus:"collecting"},"At cashier"],
  [{status:"accepted",orderStatus:"paid",collectionStatus:"collecting"},"Paid"],
  [{status:"rejected"},"Declined by staff"],
  [{status:"expired"},"Request expired"],
  [{status:"accepted",collectionStatus:"unavailable"},"Ask staff for help"]
 ])("labels %j",(value,label)=>expect(qrStatus(value)).toBe(label));
});

let root:Root|undefined;
afterEach(()=>{if(root)act(()=>root!.unmount());root=undefined;vi.useRealTimers();vi.restoreAllMocks();});
it("polls only visible focused screens, serializes refresh and stops on unmount",async()=>{
 vi.useFakeTimers();let focused=true,visible=true,finish:()=>void=()=>{};
 vi.spyOn(document,"hasFocus").mockImplementation(()=>focused);
 vi.spyOn(document,"visibilityState","get").mockImplementation(()=>visible?"visible":"hidden");
 const refresh=vi.fn(()=>new Promise<void>(resolve=>{finish=resolve;}));
 function Screen(){useVisibleRefresh(refresh);return null;}
 root=createRoot(document.createElement("div"));act(()=>root!.render(<Screen/>));
 await act(async()=>{await vi.advanceTimersByTimeAsync(15000);});expect(refresh).toHaveBeenCalledTimes(1);
 await act(async()=>{await vi.advanceTimersByTimeAsync(30000);});expect(refresh).toHaveBeenCalledTimes(1);
 await act(async()=>{finish();});focused=false;
 await act(async()=>{await vi.advanceTimersByTimeAsync(15000);});expect(refresh).toHaveBeenCalledTimes(1);
 focused=true;visible=false;window.dispatchEvent(new Event("focus"));expect(refresh).toHaveBeenCalledTimes(1);
 visible=true;await act(async()=>{document.dispatchEvent(new Event("visibilitychange"));});expect(refresh).toHaveBeenCalledTimes(2);
 act(()=>root!.unmount());root=undefined;finish();
 await vi.advanceTimersByTimeAsync(30000);expect(refresh).toHaveBeenCalledTimes(2);
});
