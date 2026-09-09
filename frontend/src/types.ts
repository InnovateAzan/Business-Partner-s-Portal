export * from "./types/index";

export type OracleReceiptLine = {
  rcvTransactionId: number;
  grnNumber: string;
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
