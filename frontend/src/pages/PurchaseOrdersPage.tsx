import { useEffect, useMemo, useState } from "react";
import { getMyPoGrns } from "../api/portal";
import { DataTableToolbar } from "../components/DataTableToolbar";
import type { OraclePoGrn } from "../types";
import { exportRowsToCsv } from "../utils/exportCsv";

function formatDate(value?: string | null) {
  if (!value) return "-";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  const day = String(date.getDate()).padStart(2, "0");
  const month = date.toLocaleString("en-US", { month: "short" });
  const year = date.getFullYear();

  return `${day}-${month}-${year}`;
}

function poDate(row: OraclePoGrn) {
  return row.poCreationDate || row.poApprovedDate || row.receiptDate || null;
}

function isWithinDateRange(
  value: string | null | undefined,
  fromDate: string,
  toDate: string
) {
  if (!fromDate && !toDate) return true;
  if (!value) return false;

  const current = new Date(value);
  if (Number.isNaN(current.getTime())) return false;

  current.setHours(0, 0, 0, 0);

  if (fromDate) {
    const from = new Date(`${fromDate}T00:00:00`);
    if (current < from) return false;
  }

  if (toDate) {
    const to = new Date(`${toDate}T23:59:59`);
    if (current > to) return false;
  }

  return true;
}

export function PurchaseOrdersPage() {
  const [rows, setRows] = useState<OraclePoGrn[]>([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");

  useEffect(() => {
    getMyPoGrns().then(setRows);
  }, []);

  const pos = useMemo(
    () =>
      [
        ...new Map(
          rows.map((row) => [row.poNumber, row])
        ).values(),
      ],
    [rows]
  );

  const statusOptions = useMemo(() => {
    const values = new Set(
      pos
        .map((row) => (row.poStatus || "Open").trim())
        .filter(Boolean)
    );

    return [...values]
      .sort((a, b) => a.localeCompare(b))
      .map((value) => ({ value, label: value }));
  }, [pos]);

  const filteredRows = useMemo(() => {
    const query = search.trim().toLowerCase();

    return pos.filter((row) => {
      const status = row.poStatus || "Open";
      const visibleText = [
        row.poNumber,
        formatDate(poDate(row)),
        status,
        row.currencyCode || "PKR",
        Number(row.poLineAmount || 0).toLocaleString(),
        Number(row.quantityAvailableToInvoice || 0).toLocaleString(),
      ]
        .join(" ")
        .toLowerCase();

      return (
        (!query || visibleText.includes(query)) &&
        (!statusFilter || status.toLowerCase() === statusFilter.toLowerCase()) &&
        isWithinDateRange(poDate(row), fromDate, toDate)
      );
    });
  }, [pos, search, statusFilter, fromDate, toDate]);

  function exportCurrentRows() {
    exportRowsToCsv(
      "purchase-orders.csv",
      [
        "PO Number",
        "PO Date",
        "Status",
        "Currency",
        "PO Amount",
        "Available To Invoice",
      ],
      filteredRows.map((row) => [
        row.poNumber,
        formatDate(poDate(row)),
        row.poStatus || "Open",
        row.currencyCode || "PKR",
        Number(row.poLineAmount || 0),
        Number(row.quantityAvailableToInvoice || 0),
      ])
    );
  }

  return (
    <div className="page-card">
      <div className="page-card-head vendor-table-head">
        <div>
          <h2>Purchase Orders</h2>
          <p>Open and eligible purchase orders from Oracle EBS.</p>
        </div>

        <DataTableToolbar
          searchValue={search}
          onSearchChange={setSearch}
          searchPlaceholder="Search Purchase Orders..."
          statusValue={statusFilter}
          onStatusChange={setStatusFilter}
          statusOptions={statusOptions}
          showDateFilter
          fromDate={fromDate}
          toDate={toDate}
          onApplyDateRange={(from, to) => {
            setFromDate(from);
            setToDate(to);
          }}
          onExport={exportCurrentRows}
        />
      </div>

      <table className="data-table vendor-data-table">
        <thead>
          <tr>
            <th>PO Number</th>
            <th>PO Date</th>
            <th>Status</th>
            <th>Currency</th>
            <th>PO Amount</th>
            <th>Available To Invoice</th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map((row) => (
            <tr key={row.poNumber}>
              <td>{row.poNumber}</td>
              <td>{formatDate(poDate(row))}</td>
              <td>
                <span className="status green">
                  {row.poStatus || "Open"}
                </span>
              </td>
              <td>{row.currencyCode || "PKR"}</td>
              <td>{Number(row.poLineAmount || 0).toLocaleString()}</td>
              <td>
                {Number(row.quantityAvailableToInvoice || 0).toLocaleString()}
              </td>
            </tr>
          ))}

          {!filteredRows.length && (
            <tr>
              <td colSpan={6} className="empty">
                No purchase orders match the selected filters.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
