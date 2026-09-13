export * from "./types/index";

export type OracleReceiptLine = {
  rcvTransactionId: number;
  grnNumber: string;
  shipmentLineId: number;
  itemId?: number | null;
  itemDescription?: string | null;
  poHeaderId: number;
  poNumber: string;
  poLineId: number;
  poLineNumber: number;
  poLineLocationId: number;
  receivedQuantity: number;
  availableQuantity: number;
  unitPrice: number;
  matchOption: string;
  extendedAmount: number;
};
