// @vitest-environment node
import {afterEach, expect, it, vi} from "vitest";
import {fetchSettlementExport, settlementExportFileName} from "./settlementExport";
const request = {branchId:"11111111-1111-4111-8111-111111111111",fromDate:"2026-09-01",toDate:"2026-09-02"};
const csv = '\uFEFF"record_type","organization_id","branch_id","gross_sales"\r\n"settlement","scope","branch","999999999999999.9999"\r\n';
afterEach(() => vi.unstubAllGlobals());
it("uses only cookie authentication and preserves exact bounded CSV bytes", async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(csv, {headers:{"Content-Type":"text/csv; charset=utf-8"}}));
  vi.stubGlobal("fetch", fetcher);
  const blob = await fetchSettlementExport(request, new AbortController().signal);
  expect(new Uint8Array(await blob.arrayBuffer())).toEqual(new TextEncoder().encode(csv));
  expect(fetcher.mock.calls[0]?.[0]).toContain("/export.csv?branchId=" + request.branchId);
  expect(fetcher.mock.calls[0]?.[1]).toMatchObject({credentials:"same-origin",cache:"no-store",headers:{Accept:"text/csv"}});
  expect(settlementExportFileName(request)).toBe(`day-settlements-${request.branchId}-2026-09-01-2026-09-02.csv`);
});
it("rejects denied, non-CSV, malformed and oversized responses without exposing diagnostics", async () => {
  for (const response of [new Response("private",{status:403}), new Response("private",{headers:{"Content-Type":"application/json"}}),
    new Response("private",{headers:{"Content-Type":"text/csv"}}),new Response("x".repeat(4*1024*1024+1),{headers:{"Content-Type":"text/csv"}})]) {
    vi.stubGlobal("fetch",vi.fn().mockResolvedValue(response));
    await expect(fetchSettlementExport(request,new AbortController().signal)).rejects.toThrow(/Settlement export|Invalid settlement/);
  }
});
it("aborts stalled reads and redirects expired sessions", async () => {
  vi.stubGlobal("location",{assign:vi.fn()});
  vi.stubGlobal("fetch",vi.fn().mockResolvedValue(new Response("private",{status:401})));
  await expect(fetchSettlementExport(request,new AbortController().signal)).rejects.toThrow("Settlement export unavailable");
  expect(location.assign).toHaveBeenCalledWith("/bff/customer/login");
  const stream=new ReadableStream<Uint8Array>({start(controller){controller.error(new DOMException("Aborted","AbortError"));}});
  vi.stubGlobal("fetch",vi.fn().mockResolvedValue(new Response(stream,{headers:{"Content-Type":"text/csv"}})));
  await expect(fetchSettlementExport(request,new AbortController().signal)).rejects.toThrow();
});
