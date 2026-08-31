import { useEffect, useRef, useState } from "react";
import { Alert, Button, Card, Descriptions, Form, Input, Modal, Select, Space, Table, Typography } from "antd";
import { ApiError, createApiClient } from "@nexaconnect/api-client";

type Review = { orderId:string; branchId:string; status:string; reason:string; resolution?:string; concurrencyVersion:number; createdAtUtc:string; updatedAtUtc:string };
type History = { id:string; action:string; reason:string; actorSubjectId:string; authorizationDecisionId:string; concurrencyVersion:number; occurredAtUtc:string };
type Decision = { resolution:string; reason:string };
const base = "/bff/customer/payment-reviews";
const api = createApiClient({ onUnauthorized:()=>location.assign("/bff/customer/login") });
const choices = [
  {value:"confirm_void",label:"Confirm void"},
  {value:"resume_payment",label:"Resume payment"},
  {value:"escalate",label:"Escalate for investigation"},
];

export function PaymentReviewPanel() {
  const [branch,setBranch] = useState("");
  const [rows,setRows] = useState<Review[]>([]);
  const [detail,setDetail] = useState<Review>();
  const [history,setHistory] = useState<History[]>([]);
  const [canResolve,setCanResolve] = useState(false);
  const [loaded,setLoaded] = useState(false);
  const [busy,setBusy] = useState(false);
  const [notice,setNotice] = useState<string>();
  const [decision,setDecision] = useState<Decision>();
  const [form] = Form.useForm<Decision>();
  const pending = useRef<AbortController>();
  useEffect(()=>()=>pending.current?.abort(),[]);
  const begin = () => { pending.current?.abort(); const controller=new AbortController(); pending.current=controller; setBusy(true); return controller.signal; };
  const clearDetail = () => { setDetail(undefined);setHistory([]);setDecision(undefined);form.resetFields(); };
  const readDetail = async(orderId:string,signal:AbortSignal) => {
    const [review,entries]=await Promise.all([
      api.request<Review>(`${base}/${orderId}`,{signal}),
      api.request<History[]>(`${base}/${orderId}/history`,{signal}),
    ]);
    if(!signal.aborted){setDetail(review);setHistory(entries);}
  };
  const load = async() => {
    const signal=begin(); clearDetail(); setRows([]);setLoaded(false);setCanResolve(false);setNotice(undefined);
    try {
      const access=await api.request<{canRead:boolean;canResolve:boolean}>(`${base}/branches/${branch}/access`,{signal});
      if(!access.canRead){if(!signal.aborted)setNotice("You do not have permission to read Payment Reviews for this branch.");return;}
      const reviews=await api.request<Review[]>(`${base}/branches/${branch}`,{signal});
      if(!signal.aborted){setRows(reviews);setCanResolve(access.canResolve);setLoaded(true);}
    } catch {if(!signal.aborted)setNotice("Unable to load Payment Reviews. Check your branch access and try again.");}
    finally {if(!signal.aborted)setBusy(false);}
  };
  const select = async(orderId:string) => {
    const signal=begin();clearDetail();setNotice(undefined);
    try {await readDetail(orderId,signal);}
    catch {if(!signal.aborted)setNotice("Review details are unavailable. Refresh the branch before continuing.");}
    finally {if(!signal.aborted)setBusy(false);}
  };
  const resolve = async() => {
    if(!detail||!decision||busy)return;
    const snapshot=detail, submitted=decision, signal=begin();
    setDecision(undefined);form.resetFields();setNotice(undefined);setDetail(undefined);setHistory([]);
    let message="Resolution saved. Review the updated state before any further action.";
    try {
      const csrf=await api.request<{requestToken:string}>(`${base}/csrf`,{signal});
      await api.request(`${base}/${snapshot.orderId}/resolve`,{
        method:"POST",signal,headers:{"X-Nexa-CSRF":csrf.requestToken},
        body:{...submitted,expectedConcurrencyVersion:snapshot.concurrencyVersion},
      });
    } catch(error) {
      message=error instanceof ApiError&&error.status===409
        ? "This review changed. Latest state reloaded; choose and confirm a new decision. Nothing was automatically retried."
        : "Resolution was not confirmed. Check the refreshed state before making another decision; the request was not retried.";
    }
    try {
      await readDetail(snapshot.orderId,signal);
      const reviews=await api.request<Review[]>(`${base}/branches/${branch}`,{signal});
      if(!signal.aborted)setRows(reviews);
    } catch {if(!signal.aborted){clearDetail();setRows([]);setLoaded(false);} message+=" Refresh failed; reload the branch to continue.";}
    finally {if(!signal.aborted){setNotice(message);setBusy(false);}}
  };
  return <Space direction="vertical" style={{width:"100%"}} size="middle">
    <Typography.Title level={2}>Payment Reviews</Typography.Title>
    <Alert type="info" message="Enter a branch UUID supplied by your administrator. Access is checked for that branch; organization-wide branch access is not required."/>
    <Form layout="inline" onFinish={load}>
      <Form.Item label="Branch UUID"><Input aria-label="Branch UUID" value={branch} disabled={busy} onChange={e=>{setBranch(e.target.value.trim());setRows([]);setLoaded(false);setCanResolve(false);clearDetail();setNotice(undefined);}}/></Form.Item>
      <Button htmlType="submit" loading={busy} disabled={!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(branch)}>Load reviews</Button>
    </Form>
    {notice&&<Alert role="status" type="warning" message={notice}/>}
    {loaded&&<>
      {!canResolve&&<Alert type="info" message="Read-only access: you cannot resolve Payment Reviews for this branch."/>}
      <Typography.Text>Up to 100 actionable open reviews, oldest first. Active resolution leases are excluded. Refresh to update ages and status.</Typography.Text>
      <Table rowKey="orderId" dataSource={rows} columns={[
        {title:"Order",dataIndex:"orderId"}, {title:"Status",dataIndex:"status"},
        {title:"Age (minutes)",render:(_,row:Review)=>Math.max(0,Math.floor((Date.now()-Date.parse(row.createdAtUtc))/60000))},
        {title:"Action",render:(_,row:Review)=><Button disabled={busy} onClick={()=>select(row.orderId)}>View review</Button>},
      ]}/>
    </>}
    {detail&&<Card title="Review details">
      <Descriptions bordered column={1}>
        <Descriptions.Item label="Order">{detail.orderId}</Descriptions.Item>
        <Descriptions.Item label="Status">{detail.status}</Descriptions.Item>
        <Descriptions.Item label="Reason">{detail.reason}</Descriptions.Item>
        <Descriptions.Item label="Last decision">{detail.resolution??"None"}</Descriptions.Item>
        <Descriptions.Item label="Version">{detail.concurrencyVersion}</Descriptions.Item>
        <Descriptions.Item label="Updated">{detail.updatedAtUtc}</Descriptions.Item>
      </Descriptions>
      <Typography.Title level={3}>Immutable decision history</Typography.Title>
      <Typography.Paragraph>Most recent 100 entries, newest first. Failed attempts are not committed decisions.</Typography.Paragraph>
      <Table rowKey="id" dataSource={history} columns={[
        {title:"When",dataIndex:"occurredAtUtc"},{title:"Decision",dataIndex:"action"},
        {title:"Reason",dataIndex:"reason"},{title:"Actor",dataIndex:"actorSubjectId"},
        {title:"Authorization decision",dataIndex:"authorizationDecisionId"},{title:"Version",dataIndex:"concurrencyVersion"},
      ]}/>
      {canResolve&&detail.status==="open"&&<Form form={form} layout="vertical" onFinish={values=>setDecision({...values,reason:values.reason.trim()})}>
        <Form.Item name="resolution" label="Decision" rules={[{required:true}]}><Select options={choices}/></Form.Item>
        <Form.Item name="reason" label="Reason for decision" rules={[{required:true,whitespace:true,max:200}]} extra="Do not include card details, provider credentials, or personal information."><Input.TextArea maxLength={200}/></Form.Item>
        <Button type="primary" htmlType="submit" disabled={busy}>Review decision</Button>
      </Form>}
    </Card>}
    <Modal title="Confirm Payment Review decision" open={!!decision} okText="Confirm decision" onOk={resolve} onCancel={()=>setDecision(undefined)} confirmLoading={busy}>
      <Typography.Paragraph>Order: {detail?.orderId}</Typography.Paragraph>
      <Typography.Paragraph>Decision: {choices.find(x=>x.value===decision?.resolution)?.label}</Typography.Paragraph>
      <Typography.Paragraph>Reason: {decision?.reason}</Typography.Paragraph>
      <Alert type="warning" message="This creates an immutable audit entry. Before confirming void, independently verify the payment outcome using the operations runbook: this action does not query the payment provider. Resume payment returns the order to payment-pending; it does not capture funds here. Escalation keeps the case open."/>
    </Modal>
  </Space>;
}
