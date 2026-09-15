import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { deletePortalInvoice, getInvoiceIssue, getMyPortalInvoices } from "../api/portal";
import { DataTableToolbar } from "../components/DataTableToolbar";
import type { PortalInvoice } from "../types";
import { exportRowsToCsv } from "../utils/exportCsv";

type InvoicePresentation = { status: string; issue?: boolean };

function presentInvoice(row: PortalInvoice): InvoicePresentation {
  const status = (row.status || "").toUpperCase();
  const integration = (row.integrationStatus || "").toUpperCase();

  if (status === "CANCELLED") return { status: "Cancelled" };
  if (status === "PAID") return { status: "Paid" };
  if (status === "APPROVED" || status === "ACCEPTED") return { status: "Approved" };
  if (status === "RETURNED") return { status: "Returned for Correction", issue: true };
  if (["ORACLE_REJECTED", "REJECTED"].includes(status)) return { status: "Rejected", issue: true };

  if (
    ["INTEGRATION_FAILED", "FAILED"].includes(status) ||
    ["FAILED", "INTEGRATION_FAILED"].includes(integration)
  ) {
    return { status: "Action Required", issue: true };
  }

  if (status === "PENDING" || status === "UNDER_FINANCE_REVIEW") {
    return { status: "Pending for Approval" };
  }

  if (status === "SENT_TO_ORACLE" || integration === "SUCCESS") {
    return { status: "Pending" };
  }

  if (
    ["PROCESSING", "RETRYING", "RESUBMITTED"].includes(status) ||
    ["PENDING", "PROCESSING", "RETRYING"].includes(integration)
  ) {
    return { status: "Processing" };
  }

  return { status: "Submitted" };
}

function statusClass(row: PortalInvoice) {
  const status = presentInvoice(row).status;

  if (
    ["Cancelled", "Rejected", "Action Required", "Returned for Correction"].includes(status)
  ) {
    return "red";
  }

  if (status === "Processing") {
    return "orange";
  }

  if (["Pending for Approval", "Approved", "Paid"].includes(status)) {
    return "green";
  }

  return "blue";
}

function canDelete(row: PortalInvoice) {
  const status = (row.status || "").toUpperCase();
  const integration = (row.integrationStatus || "").toUpperCase();

  return (
    status === "CANCELLED" ||
    ["INTEGRATION_FAILED", "FAILED", "ORACLE_REJECTED", "REJECTED"].includes(status) ||
    ["FAILED", "INTEGRATION_FAILED"].includes(integration)
  );
}

export function RequestHistoryPage() {
  const [rows, setRows] = useState<PortalInvoice[]>([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const navigate = useNavigate();
  const [issueLoading, setIssueLoading] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  async function loadRows() {
    setRows(await getMyPortalInvoices());
  }

  useEffect(() => {
    loadRows();
  }, []);

  const statusOptions = useMemo(() => {
    const options = new Map<string, string>();

    rows.forEach((row) => {
      options.set(row.status, presentInvoice(row).status);
    });

    return [...options.entries()]
      .sort((a, b) => a[1].localeCompare(b[1]))
      .map(([value, label]) => ({
        value,
        label,
      }));
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
        row.description || "-",
      ]
        .join(" ")
        .toLowerCase();

      return (
        (!query || visibleText.includes(query)) &&
        (!statusFilter ||
          row.status.toLowerCase() === statusFilter.toLowerCase())
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
        "Remarks",
        "GRNs",
        "Action",
      ],
      filteredRows.map((row) => [
        row.invoiceNumber,
        row.invoiceDate || "-",
        row.poNumber || "-",
        Number(row.invoiceAmount || 0),
        presentInvoice(row).status,
        row.description || "-",
        row.grnNumbers?.join(", ") || "-",
        presentInvoice(row).status === "Rejected"
          ? "Resubmit"
          : presentInvoice(row).issue
            ? "View Issue"
            : canDelete(row)
              ? "Delete"
              : "-",
      ])
    );
  }

  async function viewIssue(row: PortalInvoice) {
    setIssueLoading(true);

    try {
      const issue = await getInvoiceIssue(row.id);

      const query = new URLSearchParams();

      if (issue.reason) {
        query.set("issue", issue.reason);
      }

      if (issue.oracleRequestId) {
        query.set("oracleRequestId", String(issue.oracleRequestId));
      }

      if (issue.integrationStatus || issue.status) {
        query.set(
          "integrationStatus",
          issue.integrationStatus || issue.status
        );
      }

      navigate(
        `/invoices/${row.id}/resubmit${
          query.toString() ? `?${query.toString()}` : ""
        }`
      );
    } finally {
      setIssueLoading(false);
    }
  }

  async function deleteInvoice(row: PortalInvoice) {
    const confirmed = window.confirm(
      row.status.toUpperCase() === "CANCELLED"
        ? `Delete cancelled invoice ${row.invoiceNumber} from the portal?`
        : `Delete failed invoice ${row.invoiceNumber} from the portal?`
    );

    if (!confirmed) return;

    setDeletingId(row.id);

    try {
      await deletePortalInvoice(row.id);

      setRows((current) =>
        current.filter((x) => x.id !== row.id)
      );
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <div className="page-card">
      <div className="page-card-head invoice-history-head">
        <div>
          <h2>Invoice History</h2>
          <p>
            Drafts, submissions, returned invoices and resubmission status.
          </p>
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

          <Link
            className="primary-btn"
            to="/invoices/new"
          >
            + Submit Invoice
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
            <th>Remarks</th>
            <th>GRNs</th>
            <th>Action</th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map((row) => (
            <tr key={row.id}>
              <td>{row.invoiceNumber}</td>

              <td>
                {row.invoiceDate || "-"}
              </td>

              <td>
                {row.poNumber || "-"}
              </td>

              <td>
                {Number(row.invoiceAmount).toLocaleString()}
              </td>

              <td>
                <span
                  className={`status ${statusClass(row)}`}
                >
                  {presentInvoice(row).status}
                </span>
              </td>

              <td>
                {row.description || "-"}
              </td>

              <td>
                {row.grnNumbers?.join(", ") || "-"}
              </td>

              <td>
                <div
                  style={{
                    display: "flex",
                    gap: 6,
                    alignItems: "center",
                  }}
                >
                  {presentInvoice(row).issue && (
                    <button
                      className="table-btn"
                      type="button"
                      disabled={issueLoading}
                      onClick={() => viewIssue(row)}
                    >
                      {presentInvoice(row).status === "Rejected"
                        ? "Resubmit"
                        : "View Issue"}
                    </button>
                  )}

                  {canDelete(row) && (
                    <button
                      className="danger-btn"
                      type="button"
                      disabled={deletingId === row.id}
                      onClick={() => deleteInvoice(row)}
                    >
                      {deletingId === row.id
                        ? "Deleting..."
                        : "Delete"}
                    </button>
                  )}

                  {!presentInvoice(row).issue &&
                    !canDelete(row) &&
                    "-"}
                </div>
              </td>
            </tr>
          ))}

          {!filteredRows.length && (
            <tr>
              <td
                colSpan={8}
                className="empty"
              >
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