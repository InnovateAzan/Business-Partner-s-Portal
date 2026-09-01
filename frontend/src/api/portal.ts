import { api } from "./client";
import type { OracleInvoice, OraclePoGrn, OracleSupplier, PortalInvoice } from "../types";
export async function getMySupplier(){ const {data}=await api.get<OracleSupplier>("/oracle/suppliers/my"); return data; }
export async function getMyPoGrns(){ const {data}=await api.get<OraclePoGrn[]>("/oracle/po-grns/my"); return data; }
export async function getMyOracleInvoices(){ const {data}=await api.get<OracleInvoice[]>("/oracle/invoices/my"); return data; }
export async function getMyPortalInvoices(){ const {data}=await api.get<PortalInvoice[]>("/invoices/my"); return data; }
export async function submitInvoice(payload:FormData){ const {data}=await api.post("/invoices",payload,{headers:{"Content-Type":"multipart/form-data"}}); return data; }
export async function saveDraft(payload:FormData){ const {data}=await api.post("/invoices/draft",payload,{headers:{"Content-Type":"multipart/form-data"}}); return data; }
export async function resubmitInvoice(id:string,payload:FormData){ const {data}=await api.post(`/invoices/${id}/resubmit`,payload,{headers:{"Content-Type":"multipart/form-data"}}); return data; }
export async function getInvoiceHistory(id:string){ const {data}=await api.get(`/invoices/${id}/history`); return data; }
export async function startVendorRegistration(payload:{supplierNumber:string;vendorName:string;email:string}){ const {data}=await api.post("/registration/vendor/start",payload); return data as {challengeId:string; maskedEmail:string; devOtp?:string}; }
export async function completeVendorRegistration(payload:{challengeId:string;otp:string;fullName:string;password:string;confirmPassword:string}){ const {data}=await api.post("/registration/vendor/complete",payload); return data; }
export async function getIntegrationQueue(){ const {data}=await api.get("/integration/status"); return data; }
export async function retryIntegration(id:string){ const {data}=await api.post(`/integration/${id}/retry`); return data; }
export async function getAuditLogs(){ const {data}=await api.get("/admin/audit"); return data; }
