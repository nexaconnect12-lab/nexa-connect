import React, { useEffect, useState } from "react";
import { PaymentReviewPanel } from "./PaymentReviewPanel";
import { createRoot } from "react-dom/client";
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Form,
  Input,
  InputNumber,
  List,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Typography,
  Switch,
} from "antd";
import { createApiClient } from "@nexaconnect/api-client";
import { AuthorizationUiProvider } from "@nexaconnect/authorization-ui";
import { NexaDesignProvider } from "@nexaconnect/design-system";
import { ErrorBoundary, normalizeError } from "@nexaconnect/error-handling";
import { PortalLayout } from "@nexaconnect/layout";
import {
  Access,
  PortalSection,
  scopedFeaturePath,
  sections,
  selectedAccess,
  Tenant,
} from "./portal";

type Session = { subjectId: string; username?: string };
type AccessResponse = { subjectId: string; organizations: Access[] };
type Member = {
  organizationId: string;
  subjectId: string;
  status: string;
  joinedAtUtc?: string;
  concurrencyVersion: number;
};
type Branch = {
  branchId: string;
  restaurantId: string;
  code: string;
  name: string;
  timeZone: string;
  currency: string;
  status: string;
  concurrencyVersion: number;
};
type FeatureResponse = { items?: unknown[]; message?: string };
type Configuration = { branchId:string; dineInEnabled:boolean; takeawayEnabled:boolean; requireTableForDineIn:boolean; serviceChargePercent:number; concurrencyVersion:number };
type Dashboard = { completedOrders:number; grossSales:number; netPaid:number; refunded:number; currency?:string; latestGlobalCheckpointUpdatedAtUtc?:string };
type SalesReport = { items:Array<{orderId:string;branchId:string;channel:string;serviceType:string;currency:string;totalAmount:number;orderStatus:string;orderedAtUtc:string}>; totalSales:number; currency?:string; latestGlobalCheckpointUpdatedAtUtc?:string };
type MediaAsset = { id:string; originalFileName:string; contentType:string; sizeBytes:number; processingStatus:string; uploadedAtUtc:string; ownerService:string; concurrencyVersion:number };
type ActivityRecord={eventId:string;sourceService:string;actorSubjectId:string;action:string;resourceType:string;resourceId:string;outcome:string;occurredAtUtc:string;projectedAtUtc:string};
type ActivityPage={items:ActivityRecord[];nextCursor?:string};
const api = createApiClient({
  onUnauthorized: () =>
    location.assign(
      `/bff/customer/login?returnUrl=${encodeURIComponent(location.pathname + location.hash)}`,
    ),
});

function useRequest<T>(path?: string) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<string>();
  const reload = () => {
    if (path)
      api
        .request<T>(path)
        .then((v) => {
          setData(v);
          setError(undefined);
        })
        .catch((e) => setError(normalizeError(e).message));
  };
  useEffect(reload, [path]);
  return { data, error, reload };
}
function ContextPicker({
  access,
  tenant,
  onChange,
}: {
  access: Access[];
  tenant?: Tenant;
  onChange: (v: Tenant) => void;
}) {
  return (
    <Select
      style={{ minWidth: 300 }}
      value={
        tenant
          ? `${tenant.organizationId}|${tenant.applicationCode}`
          : undefined
      }
      placeholder="Select organization and product"
      options={access.map((x) => ({
        value: `${x.organizationId}|${x.applicationCode}`,
        label: `${x.organizationName} Â· ${x.applicationCode}`,
      }))}
      onChange={async (value) => {
        const [organizationId, applicationCode] = value.split("|");
        onChange(
          await api.request<Tenant>("/bff/customer/tenant", {
            method: "POST",
            body: { organizationId, applicationCode },
          }),
        );
      }}
    />
  );
}

function MembershipsPanel() {
  const state = useRequest<Member[]>("/bff/customer/memberships");
  const [form] = Form.useForm();
  const [message, setMessage] = useState<string>();
  const save = async (values: { subjectId: string; status: string }) => {
    try {
      await api.request(
        `/bff/customer/memberships/${encodeURIComponent(values.subjectId)}`,
        { method: "PUT", body: { status: values.status } },
      );
      form.resetFields();
      setMessage("Membership saved.");
      state.reload();
    } catch (e) {
      setMessage(normalizeError(e).message);
    }
  };
  const change = async (member: Member, status: string) => {
    try {
      await api.request(
        `/bff/customer/memberships/${encodeURIComponent(member.subjectId)}`,
        {
          method: "PUT",
          body: { status, expectedVersion: member.concurrencyVersion },
        },
      );
      state.reload();
    } catch (e) {
      setMessage(normalizeError(e).message);
    }
  };
  return (
    <>
      <Typography.Title level={2}>
        Customer users & memberships
      </Typography.Title>
      <Alert
        type="info"
        showIcon
        message="Identity credentials stay in Keycloak. Add users here by their stable Keycloak subject ID."
      />
      <Card title="Add membership" style={{ marginTop: 16 }}>
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item
            name="subjectId"
            label="Keycloak subject ID"
            rules={[{ required: true }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="status" label="Status" initialValue="active">
            <Select
              options={["invited", "active", "suspended"].map((value) => ({
                value,
              }))}
            />
          </Form.Item>
          <Button type="primary" htmlType="submit">
            Save membership
          </Button>
        </Form>
      </Card>
      {message && (
        <Alert
          style={{ marginTop: 16 }}
          type={message === "Membership saved." ? "success" : "error"}
          message={message}
        />
      )}
      <Table
        style={{ marginTop: 16 }}
        rowKey="subjectId"
        loading={!state.data && !state.error}
        dataSource={state.data}
        columns={[
          { title: "Subject", dataIndex: "subjectId" },
          {
            title: "Status",
            dataIndex: "status",
            render: (v) => <Tag>{v}</Tag>,
          },
          {
            title: "Joined",
            dataIndex: "joinedAtUtc",
            render: (v) => v ?? "â€”",
          },
          {
            title: "Actions",
            render: (_, member) => (
              <Space>
                <Button onClick={() => change(member, "active")}>
                  Activate
                </Button>
                <Button onClick={() => change(member, "suspended")}>
                  Suspend
                </Button>
                <Button danger onClick={() => change(member, "removed")}>
                  Remove
                </Button>
              </Space>
            ),
          },
        ]}
      />
    </>
  );
}
function BranchesPanel() {
  const state = useRequest<Branch[]>("/bff/customer/branches");
  const [form] = Form.useForm();
  const [message, setMessage] = useState<string>();
  const save = async (values: Record<string, string>) => {
    try { await api.request("/bff/customer/branches", { method: "POST", body: values }); form.resetFields(); setMessage("Branch saved."); state.reload(); }
    catch (error) { setMessage(normalizeError(error).message); }
  };
  const change = async (branch: Branch, status: string) => {
    try { await api.request(`/bff/customer/branches/${branch.branchId}`, { method: "PUT", body: { name: branch.name, timeZone: branch.timeZone, currency: branch.currency, status, expectedVersion: branch.concurrencyVersion } }); state.reload(); }
    catch (error) { setMessage(normalizeError(error).message); }
  };
  return <><Typography.Title level={2}>Branches & locations</Typography.Title><Card title="Create branch"><Form form={form} layout="vertical" onFinish={save}><Form.Item name="restaurantId" label="Restaurant ID" rules={[{required:true}]}><Input/></Form.Item><Form.Item name="code" label="Code" rules={[{required:true,pattern:/^[a-z0-9][a-z0-9_-]{0,63}$/}]}><Input/></Form.Item><Form.Item name="name" label="Name" rules={[{required:true}]}><Input/></Form.Item><Form.Item name="timeZone" label="IANA time zone" initialValue="Asia/Singapore" rules={[{required:true}]}><Input/></Form.Item><Form.Item name="currency" label="Currency" initialValue="SGD" rules={[{required:true,pattern:/^[A-Z]{3}$/}]}><Input/></Form.Item><Button type="primary" htmlType="submit">Create</Button></Form></Card>{message&&<Alert style={{marginTop:16}} message={message}/>}<Table style={{marginTop:16}} rowKey="branchId" dataSource={state.data} columns={[{title:"Code",dataIndex:"code"},{title:"Name",dataIndex:"name"},{title:"Time zone",dataIndex:"timeZone"},{title:"Currency",dataIndex:"currency"},{title:"Status",dataIndex:"status",render:value=><Tag>{value}</Tag>},{title:"Actions",render:(_,branch)=><Space><Button onClick={()=>change(branch,"active")}>Activate</Button><Button onClick={()=>change(branch,"suspended")}>Suspend</Button><Button danger onClick={()=>change(branch,"closed")}>Close</Button></Space>}]} /></>;
}
function BranchPicker({value,onChange}:{value?:string;onChange:(value:string)=>void}) { const branches=useRequest<Branch[]>("/bff/customer/branches"); return <Select style={{minWidth:280}} loading={!branches.data&&!branches.error} value={value} placeholder="All branches" allowClear options={branches.data?.map(branch=>({value:branch.branchId,label:`${branch.name} (${branch.code})`}))} onChange={onChange}/>; }
function ConfigurationPanel(){const branches=useRequest<Branch[]>("/bff/customer/branches"),[branchId,setBranchId]=useState<string>(),state=useRequest<Configuration>(branchId?`/bff/customer/configuration/branches/${branchId}`:undefined),[message,setMessage]=useState<string>();useEffect(()=>{const first=branches.data?.[0];if(!branchId&&first)setBranchId(first.branchId)},[branches.data,branchId]);const save=async(values:Omit<Configuration,"branchId"|"concurrencyVersion">)=>{if(!branchId||!state.data)return;try{await api.request(`/bff/customer/configuration/branches/${branchId}`,{method:"PUT",body:{...values,expectedVersion:state.data.concurrencyVersion}});setMessage("Configuration saved.");state.reload()}catch(error){setMessage(normalizeError(error).message)}};return <><Typography.Title level={2}>Product configuration</Typography.Title><Select style={{minWidth:320,marginBottom:16}} value={branchId} options={branches.data?.map(branch=>({value:branch.branchId,label:branch.name}))} onChange={setBranchId}/>{state.error&&<Alert type="error" message={state.error}/>} {state.data&&<Card><Form key={state.data.concurrencyVersion} layout="vertical" initialValues={state.data} onFinish={save}><Form.Item name="dineInEnabled" label="Dine-in" valuePropName="checked"><Switch/></Form.Item><Form.Item name="takeawayEnabled" label="Takeaway" valuePropName="checked"><Switch/></Form.Item><Form.Item name="requireTableForDineIn" label="Require table for dine-in" valuePropName="checked"><Switch/></Form.Item><Form.Item name="serviceChargePercent" label="Service charge (%)"><InputNumber min={0} max={100} precision={2}/></Form.Item><Button type="primary" htmlType="submit">Save configuration</Button></Form></Card>}{message&&<Alert style={{marginTop:16}} message={message} type={message==="Configuration saved."?"success":"error"}/>}</>}
function DashboardPanel(){const[branchId,setBranchId]=useState<string>(),state=useRequest<Dashboard>(branchId?`/bff/customer/dashboard?branchId=${branchId}`:undefined);return <><Typography.Title level={2}>Product dashboards</Typography.Title><BranchPicker value={branchId} onChange={value=>setBranchId(value)}/>{state.error&&<Alert type="error" message={state.error}/>} {state.data&&<><Descriptions bordered style={{marginTop:16}}><Descriptions.Item label="Completed orders">{state.data.completedOrders}</Descriptions.Item><Descriptions.Item label="Gross sales">{state.data.currency??""} {state.data.grossSales}</Descriptions.Item><Descriptions.Item label="Net paid">{state.data.currency??""} {state.data.netPaid}</Descriptions.Item><Descriptions.Item label="Refunded">{state.data.currency??""} {state.data.refunded}</Descriptions.Item></Descriptions><Alert style={{marginTop:16}} type="info" message={`Latest global projector checkpoint: ${state.data.latestGlobalCheckpointUpdatedAtUtc??"No projector checkpoint yet"}`}/></>}</>}
function ReportsPanel(){const[branchId,setBranchId]=useState<string>(),state=useRequest<SalesReport>(branchId?`/bff/customer/reports/sales?branchId=${branchId}`:undefined);return <><Typography.Title level={2}>Sales report</Typography.Title><BranchPicker value={branchId} onChange={value=>setBranchId(value)}/>{state.data&&<><Typography.Paragraph style={{marginTop:16}}>Completed sales total: {state.data.currency??""} {state.data.totalSales}</Typography.Paragraph><Table rowKey="orderId" dataSource={state.data.items} columns={[{title:"Ordered",dataIndex:"orderedAtUtc"},{title:"Channel",dataIndex:"channel"},{title:"Service",dataIndex:"serviceType"},{title:"Status",dataIndex:"orderStatus",render:value=><Tag>{value}</Tag>},{title:"Total",render:(_,row)=>`${row.currency} ${row.totalAmount}`}]}/><Alert type="info" message={`Latest global projector checkpoint: ${state.data.latestGlobalCheckpointUpdatedAtUtc??"No projector checkpoint yet"}`}/></>}{state.error&&<Alert type="error" message={state.error}/>}</>}
function MediaPanel(){const state=useRequest<MediaAsset[]>("/bff/customer/media"),[file,setFile]=useState<File>(),[ownerId,setOwnerId]=useState(""),[message,setMessage]=useState<string>();const upload=async()=>{if(!file||!ownerId)return;try{const bytes=new Uint8Array(await crypto.subtle.digest("SHA-256",await file.arrayBuffer())),hash=Array.from(bytes).map(v=>v.toString(16).padStart(2,"0")).join(""),checksum=btoa(String.fromCharCode(...bytes));const session=await api.request<{asset:MediaAsset;uploadUrl:string}>("/bff/customer/media/uploads",{method:"POST",body:{ownerService:"catalog",ownerType:"product",ownerId,fileName:file.name,contentType:file.type,sizeBytes:file.size,checksumSha256:hash}});const put=await fetch(session.uploadUrl,{method:"PUT",headers:{"Content-Type":file.type,"x-amz-checksum-sha256":checksum},body:file});if(!put.ok)throw new Error("Object upload failed.");await api.request(`/bff/customer/media/${session.asset.id}/complete`,{method:"POST",body:{expectedVersion:session.asset.concurrencyVersion}});setMessage("Upload completed; thumbnail and display variants are processing.");state.reload()}catch(e){setMessage(normalizeError(e).message)}};const download=async(asset:MediaAsset,variant?:"thumbnail"|"display")=>{try{const path=variant?`/bff/customer/media/${asset.id}/variants/${variant}/download`:`/bff/customer/media/${asset.id}/download`,value=await api.request<{downloadUrl:string}>(path);location.assign(value.downloadUrl)}catch(e){setMessage(normalizeError(e).message)}};const remove=async(asset:MediaAsset)=>{await api.request(`/bff/customer/media/${asset.id}?expectedVersion=${asset.concurrencyVersion}`,{method:"DELETE"});state.reload()};return <><Typography.Title level={2}>Media management</Typography.Title><Card title="Upload product image"><Space direction="vertical"><Input placeholder="Catalog product UUID" value={ownerId} onChange={e=>setOwnerId(e.target.value)}/><input type="file" accept="image/jpeg,image/png,image/webp" onChange={e=>setFile(e.target.files?.[0])}/><Button type="primary" disabled={!file||!ownerId} onClick={upload}>Upload</Button></Space></Card>{message&&<Alert style={{marginTop:16}} message={message}/>}<Table style={{marginTop:16}} rowKey="id" loading={!state.data&&!state.error} dataSource={state.data} columns={[{title:"File",dataIndex:"originalFileName"},{title:"Type",dataIndex:"contentType"},{title:"Owner",dataIndex:"ownerService"},{title:"Size (bytes)",dataIndex:"sizeBytes"},{title:"Status",dataIndex:"processingStatus",render:value=><Tag>{value}</Tag>},{title:"Uploaded",dataIndex:"uploadedAtUtc"},{title:"Actions",render:(_,asset)=><Space wrap><Button disabled={asset.processingStatus!=="ready"} onClick={()=>download(asset)}>Original</Button><Button disabled={asset.processingStatus!=="ready"} onClick={()=>download(asset,"thumbnail")}>Thumbnail</Button><Button disabled={asset.processingStatus!=="ready"} onClick={()=>download(asset,"display")}>Display</Button><Button danger onClick={()=>remove(asset)}>Delete</Button></Space>}]}/>{state.error&&<Alert type="error" message={state.error}/>}</>}
function ActivityPanel(){const[items,setItems]=useState<ActivityRecord[]>([]),[next,setNext]=useState<string>(),[submitted,setSubmitted]=useState<Record<string,string>>({}),[loading,setLoading]=useState(false),[error,setError]=useState<string>(),[form]=Form.useForm();const load=async(filters:Record<string,string>,cursor?:string,append=false)=>{setLoading(true);try{const query=new URLSearchParams(filters);if(cursor)query.set("cursor",cursor);const page=await api.request<ActivityPage>(`/bff/customer/activity?${query}`);setItems(current=>append?[...current,...page.items]:page.items);setNext(page.nextCursor);setSubmitted(filters);setError(undefined)}catch(e){setError(normalizeError(e).message)}finally{setLoading(false)}};useEffect(()=>{void load({})},[]);return <><Typography.Title level={2}>Activity projection preview</Typography.Title><Alert type="warning" message="This non-authoritative preview is empty by default until trusted service events are delivered. Do not use it as evidence that no activity occurred."/><Card style={{marginTop:16}}><Form form={form} layout="inline" onFinish={values=>load(Object.fromEntries(Object.entries(values).filter(([,v])=>v)) as Record<string,string>)}><Form.Item name="actorSubjectId" label="Actor"><Input allowClear/></Form.Item><Form.Item name="action" label="Action"><Input allowClear/></Form.Item><Button type="primary" htmlType="submit">Filter</Button></Form></Card>{error&&<Alert style={{marginTop:16}} type="error" message={error}/>}<Table style={{marginTop:16}} loading={loading} pagination={false} rowKey="eventId" dataSource={items} columns={[{title:"When",dataIndex:"occurredAtUtc"},{title:"Service",dataIndex:"sourceService"},{title:"Actor",dataIndex:"actorSubjectId"},{title:"Action",dataIndex:"action"},{title:"Resource",render:(_,item)=>`${item.resourceType}: ${item.resourceId}`},{title:"Outcome",dataIndex:"outcome",render:value=><Tag>{value}</Tag>}]} />{next&&<Button style={{marginTop:16}} loading={loading} onClick={()=>load(submitted,next,true)}>Load more</Button>}</>}
function FeaturePanel({
  section,
}: {
  section: Exclude<
    PortalSection,
    "profile" | "products" | "users" | "configuration" | "branches" | "dashboards" | "reports" | "media" | "activity" | "payment-reviews"
  >;
}) {
  const state = useRequest<FeatureResponse>(scopedFeaturePath(section));
  return (
    <>
      <Typography.Title level={2}>{section}</Typography.Title>
      {state.error && <Alert type="warning" message={state.error} />}{" "}
      {!state.data ? (
        <Spin />
      ) : (
        <Card>
          <Alert type="info" message={state.data.message} />
          <Empty />
        </Card>
      )}
    </>
  );
}

function App() {
  const session = useRequest<Session>("/bff/customer/me"),
    accessState = useRequest<AccessResponse>("/bff/customer/access");
  const [tenant, setTenant] = useState<Tenant>();
  const [section, setSection] = useState<PortalSection>(() =>
    sections.includes(location.hash.slice(1) as PortalSection)
      ? (location.hash.slice(1) as PortalSection)
      : "profile",
  );
  useEffect(() => {
    api
      .request<Tenant>("/bff/customer/tenant")
      .then(setTenant)
      .catch(() => undefined);
  }, []);
  if (!session.data || !accessState.data)
    return session.error || accessState.error ? (
      <Alert type="error" message={session.error ?? accessState.error} />
    ) : (
      <Spin fullscreen />
    );
  const access = accessState.data.organizations,
    current = selectedAccess(access, tenant);
  const labels: Record<PortalSection, string> = {
    profile: "Organization profile",
    products: "Enabled products",
    users: "Customer users & memberships",
    configuration: "Product configuration",
    branches: "Branches & locations",
    dashboards: "Product dashboards",
    reports: "Reports",
    media: "Media management",
    activity: "Activity & audit history",
    "payment-reviews": "Payment Reviews",
  };
  const nav = sections.filter(key=>key!=="payment-reviews"||tenant?.applicationCode==="nexa_connect").map((key) => ({
    key,
    label: labels[key],
    onSelect: () => {
      location.hash = key;
      setSection(key);
    },
  }));
  let panel: React.ReactNode;
  if (!tenant || !current)
    panel = (
      <Card>
        <Typography.Title level={2}>Choose your workspace</Typography.Title>
        <ContextPicker access={access} tenant={tenant} onChange={setTenant} />
      </Card>
    );
  else if (section === "profile")
    panel = (
      <>
        <Typography.Title level={2}>Organization profile</Typography.Title>
        <Descriptions bordered>
          <Descriptions.Item label="Organization">
            {current.organizationName}
          </Descriptions.Item>
          <Descriptions.Item label="Code">
            {current.organizationCode}
          </Descriptions.Item>
          <Descriptions.Item label="Organization ID">
            {current.organizationId}
          </Descriptions.Item>
          <Descriptions.Item label="Product">
            <Tag>{current.applicationCode}</Tag>
          </Descriptions.Item>
        </Descriptions>
      </>
    );
  else if (section === "products")
    panel = (
      <>
        <Typography.Title level={2}>Enabled products</Typography.Title>
        <List
          dataSource={access.filter(
            (x) => x.organizationId === current.organizationId,
          )}
          renderItem={(x) => <List.Item>{x.applicationCode}</List.Item>}
        />
      </>
    );
  else if (section === "users") panel = <MembershipsPanel />;
  else if (section === "configuration") panel = <ConfigurationPanel />;
  else if (section === "branches") panel = <BranchesPanel />;
  else if (section === "dashboards") panel = <DashboardPanel />;
  else if (section === "reports") panel = <ReportsPanel />;
  else if (section === "media") panel = <MediaPanel />;
  else if (section === "activity") panel = <ActivityPanel />;
  else if (section === "payment-reviews") panel = tenant.applicationCode==="nexa_connect"
    ? <PaymentReviewPanel key={`${tenant.organizationId}|${tenant.applicationCode}`} />
    : <Alert type="warning" message="Payment Reviews require the NexaConnect product workspace."/>;
  else panel = <FeaturePanel section={section} />;
  return (
    <AuthorizationUiProvider can={() => true}>
      <PortalLayout
        title="Customer Portal"
        items={nav}
        selectedKey={section}
        headerActions={
          <Space>
            <ContextPicker
              access={access}
              tenant={tenant}
              onChange={setTenant}
            />
            <span>{session.data.username ?? session.data.subjectId}</span>
            <Button href="/bff/customer/logout">Sign out</Button>
          </Space>
        }
      >
        {panel}
      </PortalLayout>
    </AuthorizationUiProvider>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <NexaDesignProvider>
      <ErrorBoundary
        fallback={() => (
          <Alert type="error" message="The portal could not render safely." />
        )}
      >
        <App />
      </ErrorBoundary>
    </NexaDesignProvider>
  </React.StrictMode>,
);
