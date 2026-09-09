import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { getMyPortalInvoices } from "../api/portal";
import { DataTableToolbar } from "../components/DataTableToolbar";
import type { PortalInvoice } from "../types";
import { exportRowsToCsv } from "../utils/exportCsv";

type InvoicePresentation = { status: string; issue?: string };

function presentInvoice(row: PortalInvoice): InvoicePresentation {
  const status = (row.status || "").toUpperCase();
  const integration = (row.integrationStatus || "").toUpperCase();
  if (status === "PAID") return { status: "Paid" };
  if (status === "CANCELLED") return { status: "Cancelled" };
  if (status === "RETURNED") return { status: "Returned for Correction", issue: "Invoice was returned by Finance. Please review and resubmit." };
  if (["INTEGRATION_FAILED", "FAILED", "ORACLE_REJECTED"].includes(status) || ["FAILED", "INTEGRATION_FAILED"].includes(integration)) return { status: "Action Required", issue: "This invoice needs correction before it can continue to Finance." };
  if (status === "SENT_TO_ORACLE" || integration === "SUCCESS") return { status: "Sent to Oracle" };
  if (status.includes("FINANCE")) return { status: "Under Finance Review" };
  if (["PENDING", "PROCESSING", "RETRYING", "RESUBMITTED"].includes(status) || ["PENDING", "PROCESSING", "RETRYING"].includes(integration)) return { status: "Processing" };
  return { status: "Submitted" };
}

export function RequestHistoryPage() {
  const [rows, setRows] = useState<PortalInvoice[]>([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  useEffect(() => {
    getMyPortalInvoices().then(setRows);
  }, []);

  const statusOptions = useMemo(() => {
    const values = new Set(rows.map((row) => row.status).filter(Boolean));

    return [...values]
      .sort((a, b) => a.localeCompare(b))
      .map((value) => ({ value, label: presentInvoice(rows.find(row => row.status === value)!).status }));
  }, [rows]);

  const filteredRows = useMemo(() => {
    const query = search.trim().toLowerCase();

    return rows.filter((row) => {
      const visibleText = [
        row.invoiceNumber,
        row.invoiceDate || "-",
        row.poNumber || "-",
        Number(row.invoiceAmount || 0).toLocaleString(),
        presentInvoice(row).status,
        row.grnNumbers?.join(", ") || "-",
      ]
        .join(" ")
        .toLowerCase();

      return (
        (!query || visibleText.includes(query)) &&
        (!statusFilter || row.status.toLowerCase() === statusFilter.toLowerCase())
      );
    });
  }, [rows, search, statusFilter]);

  function exportCurrentRows() {
    exportRowsToCsv(
      "invoice-history.csv",
      [
        "Invoice #",
        "Date",
        "PO",
        "Amount",
        "Status",
        "GRNs",
        "Action",
      ],
      filteredRows.map((row) => [
        row.invoiceNumber,
        row.invoiceDate || "-",
        row.poNumber || "-",
        Number(row.invoiceAmount || 0),
        presentInvoice(row).status,
        row.grnNumbers?.join(", ") || "-",
        presentInvoice(row).issue ? "View Issue" : "-",
      ])
    );
  }

  return (
    <div className="page-card">
      <div className="page-card-head invoice-history-head">
        <div>
          <h2>Invoice History</h2>
          <p>Drafts, submissions, returned invoices and resubmission status.</p>
        </div>

        <div className="invoice-history-actions">
          <DataTableToolbar
            searchValue={search}
            onSearchChange={setSearch}
            searchPlaceholder="Search Invoices..."
            statusValue={statusFilter}
            onStatusChange={setStatusFilter}
            statusOptions={statusOptions}
            onExport={exportCurrentRows}
          />

          <Link className="primary-btn" to="/invoices/new">
            + Create Invoice
          </Link>
        </div>
      </div>

      <table className="data-table vendor-data-table">
        <thead>
          <tr>
            <th>Invoice #</th>
            <th>Date</th>
            <th>PO</th>
            <th>Amount</th>
            <th>Status</th>
            <th>GRNs</th>
            <th>Action</th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map((row) => (
            <tr key={row.id}>
              <td>{row.invoiceNumber}</td>
              <td>{row.invoiceDate || "-"}</td>
              <td>{row.poNumber || "-"}</td>
              <td>{Number(row.invoiceAmount).toLocaleString()}</td>
              <td>
                <span
                  className={`status ${
                    row.status === "RETURNED"
                      ? "red"
                      : row.status === "DRAFT"
                        ? "blue"
                        : "green"
                  }`}
                >
                  {presentInvoice(row).status}
                </span>
              </td>
              <td>{row.grnNumbers?.join(", ") || "-"}</td>
              <td>
                {presentInvoice(row).issue ? (
                  <Link
                    className="table-btn"
                    to={`/invoices/${row.id}/resubmit?issue=${encodeURIComponent(presentInvoice(row).issue!)}`}
                  >
                    View Issue
                  </Link>
                ) : (
                  "-"
                )}
              </td>
            </tr>
          ))}

          {!filteredRows.length && (
            <tr>
              <td colSpan={7} className="empty">
                {rows.length
                  ? "No invoices match the selected filters."
                  : "No portal invoice submissions yet."}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
