import { api } from "./client";
import type { InvoiceIssue, OracleInvoice, OraclePoGrn, OracleSupplier, PortalInvoice, OracleReceiptLine } from "../types";
const receiptLineRequests = new Map<string, Promise<OracleReceiptLine[]>>();
export async function getMySupplier(){ const {data}=await api.get<OracleSupplier>("/oracle/suppliers/my"); return data; }
export async function getMyPoGrns(){ const {data}=await api.get<OraclePoGrn[]>("/oracle/po-grns/my"); return data; }
export async function getMyOracleInvoices(){ const {data}=await api.get<OracleInvoice[]>("/oracle/invoices/my"); return data; }
export async function getMyPortalInvoices(){ const {data}=await api.get<PortalInvoice[]>("/invoices/my"); return data; }
export async function submitInvoice(payload:FormData,idempotencyKey?:string){ const {data}=await api.post("/invoices",payload,{headers:{"Content-Type":"multipart/form-data",...(idempotencyKey?{"Idempotency-Key":idempotencyKey}:{})}}); return data; }
export async function saveDraft(payload:FormData){ const {data}=await api.post("/invoices/draft",payload,{headers:{"Content-Type":"multipart/form-data"}}); return data; }
export async function resubmitInvoice(
  id: string,
  payload: FormData,
  idempotencyKey?: string
) {
  const { data } = await api.post(
    `/invoices/${id}/resubmit`,
    payload,
    {
      headers: {
        "Content-Type": "multipart/form-data",
        ...(idempotencyKey
          ? { "Idempotency-Key": idempotencyKey }
          : {})
      }
    }
  );

  return data;
}
export async function getInvoiceHistory(id:string){ const {data}=await api.get(`/invoices/${id}/history`); return data; }
export async function getInvoiceIssue(id:string){ const {data}=await api.get<InvoiceIssue>(`/invoices/${id}/issue`); return data; }
export async function deletePortalInvoice(id:string){ const {data}=await api.delete(`/invoices/${id}`); return data; }
export async function startVendorRegistration(payload:{supplierNumber:string;vendorName:string;email:string}){ const {data}=await api.post("/registration/vendor/start",payload); return data as {challengeId:string; maskedEmail:string; devOtp?:string}; }
export async function completeVendorRegistration(payload:{challengeId:string;otp:string;fullName:string;password:string;confirmPassword:string}){ const {data}=await api.post("/registration/vendor/complete",payload); return data; }
export async function getIntegrationQueue(){ const {data}=await api.get("/integration/status"); return data; }
export async function retryIntegration(id:string){ const {data}=await api.post(`/integration/${id}/retry`); return data; }
export async function getAuditLogs(){ const {data}=await api.get("/admin/audit"); return data; }
export function getMyReceiptLines(poNumber:string,grnNumbers:string[]=[]){ const normalizedGrns=[...new Set(grnNumbers.map(x=>x.trim()).filter(Boolean))].sort(); const key=`${poNumber.trim()}|${normalizedGrns.join(",")}`; const pending=receiptLineRequests.get(key); if(pending)return pending; const request=api.get<OracleReceiptLine[]>("/oracle/receipt-lines/my",{params:{poNumber:poNumber.trim(),grnNumbers:normalizedGrns.join(",")}}).then(({data})=>data).finally(()=>receiptLineRequests.delete(key)); receiptLineRequests.set(key,request); return request; }
export async function getLiveDashboard(){ const {data}=await api.get("/dashboard/live"); return data; }
