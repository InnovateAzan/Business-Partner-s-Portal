import {
  api,
} from "./client";

export type QcPendingGrn = {
  portalVendorId: string;

  vendorCode: string;

  vendorName: string;

  oracleVendorId: string;

  poNumber: string;

  prNumber?: string | null;

  // NEW
  poDate?: string | null;

  grnNumber: string;

  grnDate?: string | null;

  poLineNumber?: string | null;

  itemCode?: string | null;

  itemDescription?: string | null;

  uom?: string | null;

  receivedQuantity?: number | null;

  qcStatus: string;

  oracleQcStatus?: string | null;

  agingDays: number;
};

export async function getQcPendingGrns() {
  const {
    data,
  } =
    await api.get<
      QcPendingGrn[]
    >(
      "/grns/qc-pending"
    );

  return data;
}