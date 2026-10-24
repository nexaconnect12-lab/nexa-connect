import {expect} from '@playwright/test';

// Acceptance reader for the documented fully-quoted CSV; never imports financial data.
export function readQuotedCsv(text){
 text=text.replace(/^\uFEFF/,'');const rows=[];let row=[],at=0;
 while(at<text.length){
  if(text[at++]!=='"')throw new Error('Expected quoted CSV evidence');let cell='',closed=false;
  while(at<text.length){const value=text[at++];if(value!=='"'){cell+=value;continue;}if(text[at]==='"'){cell+='"';at++;continue;}closed=true;break;}
  if(!closed)throw new Error('Incomplete CSV evidence');row.push(cell);
  if(text[at]===','){at++;continue;}
  if(text.slice(at,at+2)!=='\r\n')throw new Error('Expected CSV row ending');at+=2;rows.push(row);row=[];
 }
 const columns=rows.shift();if(!columns||row.length||rows.some(r=>r.length!==columns.length))throw new Error('Invalid CSV shape');
 return rows.map(r=>Object.fromEntries(columns.map((name,index)=>[name,r[index]])));
}

export async function verifyCsv(response,receipt,scope,delivery='matched'){
 expect(response.status()).toBe(200);expect(response.headers()['content-type']).toContain('text/csv');
 expect(response.headers()['cache-control']).toContain('no-store');expect(response.headers()['content-disposition']).toContain('attachment');
 expect(response.headers()['content-disposition']).toContain(scope.branchId);
 const rows=readQuotedCsv(await response.text());
 expect(rows.filter(r=>r.record_type==='observation')).toHaveLength(1);
 const settlements=rows.filter(r=>r.record_type==='settlement');expect(settlements).toHaveLength(1);
 const sale=settlements[0];expect(sale.event_id).toBe(receipt.eventId);expect(sale.approval_id).toBe(receipt.approvalId);
 expect(sale.delivery_status).toBe(delivery);expect(sale.report_status).toBe(delivery==='matched'?'matched':'gaps');
 expect(sale.correction_evidence).toBe('verified');expect(sale.contract_version).toBe('2');
 for(const [field,key] of Object.entries({gross_sales:'grossSales',completed_refunds:'completedRefunds',net_sales:'netSales',cash_variance:'cashVariance'}))expect(Number(sale[field])).toBe(receipt.snapshot[key]);
 expect(rows.filter(r=>r.record_type==='source_seal')).toHaveLength(3);
 for(const row of rows){expect(row.organization_id).toBe(scope.organizationId);expect(row.branch_id).toBe(scope.branchId);expect(row.manifest_hash).toMatch(/^[a-f0-9]{64}$/i);expect(Date.parse(row.compared_at_utc)).not.toBeNaN();if(row.record_type!=='settlement')for(const field of ['gross_sales','net_sales','cash_variance'])expect(row[field]).toBe('');}
 return rows;
}
