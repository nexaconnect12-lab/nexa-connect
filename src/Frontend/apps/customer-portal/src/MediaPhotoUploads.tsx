import {useEffect,useRef,useState} from "react";
import {Alert,Button,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {signInToCustomer} from "./navigation";

const api = createApiClient({onUnauthorized:signInToCustomer});
const allowedTypes = ["image/jpeg", "image/png", "image/webp"];
const maximumFiles = 10;
const maximumBytes = 10 * 1024 * 1024;
type UploadResult = {name:string; status:string};

async function uploadPhoto(file:File, organizationId:string, signal:AbortSignal) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", await file.arrayBuffer()));
  if (signal.aborted) throw new DOMException("Upload cancelled", "AbortError");
  const checksumSha256 = Array.from(digest, value => value.toString(16).padStart(2, "0")).join("");
  const session = await api.request<{asset:{id:string; concurrencyVersion:number}; uploadUrl:string}>("/bff/customer/media/uploads", {
    method:"POST", signal, body:{ownerService:"media", ownerType:"photo_library", ownerId:organizationId,
      fileName:file.name, contentType:file.type, sizeBytes:file.size, checksumSha256}
  });
  const target = new URL(session.uploadUrl);
  if (!["http:", "https:"].includes(target.protocol) || target.username || target.password) throw new Error("Invalid upload URL");
  const response = await fetch(target, {
    method:"PUT", credentials:"omit", redirect:"error", signal, body:file,
    headers:{"Content-Type":file.type, "x-amz-checksum-sha256":btoa(String.fromCharCode(...digest))}
  });
  if (!response.ok) throw new Error("Upload transfer failed");
  await api.request(`/bff/customer/media/${encodeURIComponent(session.asset.id)}/complete`, {
    method:"POST", signal, body:{expectedVersion:session.asset.concurrencyVersion}
  });
}

export function MediaPhotoUploads({organizationId,onFinished}:{organizationId:string; onFinished:()=>void}) {
  const [files,setFiles] = useState<File[]>([]);
  const [results,setResults] = useState<UploadResult[]>([]);
  const [notice,setNotice] = useState<string>();
  const [busy,setBusy] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const active = useRef<AbortController>();

  useEffect(() => () => {
    active.current?.abort();
    active.current = undefined;
  }, []);

  const start = async () => {
    if (active.current || files.length === 0) return;
    const batch = new AbortController();
    active.current = batch;
    setBusy(true);
    setResults(files.map(file => ({name:file.name, status:"Queued"})));
    const report = (index:number, status:string) => {
      if (active.current === batch)
        setResults(current => current.map((result, i) => i === index ? {...result, status} : result));
    };
    try {
      for (const [index,file] of files.entries()) {
        if (batch.signal.aborted) return;
        if (!allowedTypes.includes(file.type) || file.size < 1 || file.size > maximumBytes) {
          report(index, "Choose a JPEG, PNG or WebP up to 10 MiB.");
          continue;
        }
        report(index, "Uploading");
        const item = new AbortController();
        const abort = () => item.abort();
        batch.signal.addEventListener("abort", abort, {once:true});
        const timeout = window.setTimeout(abort, 60_000);
        try {
          await uploadPhoto(file, organizationId, item.signal);
          report(index, "Uploaded. Processing for the library.");
        } catch {
          report(index, "Upload unconfirmed. Check the library before trying again.");
        } finally {
          window.clearTimeout(timeout);
          batch.signal.removeEventListener("abort", abort);
        }
      }
    } finally {
      if (active.current === batch) {
        active.current = undefined;
        setBusy(false);
        setFiles([]);
        if (input.current) input.current.value = "";
        onFinished();
      }
    }
  };

  return <section aria-label="Upload photos to shared library">
    <Typography.Title level={4}>Upload photos</Typography.Title>
    <Typography.Paragraph>Choose up to {maximumFiles} JPEG, PNG or WebP photos, up to 10 MiB each.</Typography.Paragraph>
    <Space wrap>
      <label>Choose photos <input ref={input} type="file" multiple accept={allowedTypes.join(",")} disabled={busy} onChange={event => {
        const selected = Array.from(event.target.files ?? []);
        setResults([]);
        setNotice(selected.length > maximumFiles ? `Choose at most ${maximumFiles} photos at a time.` : undefined);
        setFiles(selected.length > maximumFiles ? [] : selected);
      }}/></label>
      <Button type="primary" disabled={busy || files.length === 0} onClick={() => void start()}>Upload photos</Button>
    </Space>
    {notice && <Alert type="warning" message={notice}/>}
    {results.length > 0 && <ul aria-live="polite">{results.map((result,index) => <li key={index}>{result.name} — {result.status}</li>)}</ul>}
  </section>;
}
