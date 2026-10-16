const maximumBytes = 4 * 1024 * 1024;
export type SettlementExportRequest = { branchId: string; fromDate: string; toDate: string };

export function settlementExportFileName(request: SettlementExportRequest) {
  return `day-settlements-${request.branchId}-${request.fromDate}-${request.toDate}.csv`;
}

export async function fetchSettlementExport(request: SettlementExportRequest, signal: AbortSignal): Promise<Blob> {
  const response = await fetch("/bff/customer/reports/day-settlements/export.csv?" + new URLSearchParams(request), {
    credentials: "same-origin", cache: "no-store", signal, headers: { Accept: "text/csv" }
  });
  if (response.status === 401) location.assign("/bff/customer/login");
  if (!response.ok || response.headers.get("content-type")?.split(";")[0]?.trim() !== "text/csv" || !response.body) {
    await response.body?.cancel();
    throw new Error("Settlement export unavailable");
  }
  const reader = response.body.getReader(), chunks: Uint8Array[] = [];
  let bytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      bytes += value.byteLength;
      if (bytes > maximumBytes) throw new Error("Settlement export too large");
      chunks.push(value);
    }
  } catch (error) {
    await reader.cancel();
    throw error;
  } finally { reader.releaseLock(); }
  const blob = new Blob(chunks, { type: "text/csv;charset=utf-8" });
  if (!(await blob.slice(0, 128).text()).startsWith('"record_type","organization_id","branch_id",'))
    throw new Error("Invalid settlement export");
  return blob;
}

export function saveSettlementExport(blob: Blob, request: SettlementExportRequest) {
  const url = URL.createObjectURL(blob), anchor = document.createElement("a");
  anchor.href = url; anchor.download = settlementExportFileName(request);
  document.body.appendChild(anchor); anchor.click(); anchor.remove();
  // Give the browser time to acquire the blob, then release its financial bytes.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
