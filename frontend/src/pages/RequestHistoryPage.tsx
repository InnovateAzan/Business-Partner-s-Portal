import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  deletePortalInvoice,
  downloadInvoiceDocument,
  getInvoiceDocuments,
  getInvoiceIssue,
  getMyPortalInvoices,
  getPortalInvoice,
  viewInvoiceDocument,
} from "../api/portal";
import { DataTableToolbar } from "../components/DataTableToolbar";
import type { PortalInvoice } from "../types";
import type { InvoiceDocumentDto } from "../api/portal";
import { exportRowsToCsv } from "../utils/exportCsv";

type InvoicePresentation = {
  status: string;
  issue?: boolean;
};

function formatDate(value?: string | null) {
  if (!value) return "-";

  const datePart = value.substring(0, 10);
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(datePart);

  if (match) {
    const [, year, month, day] = match;
    const date = new Date(
      Number(year),
      Number(month) - 1,
      Number(day)
    );

    return date.toLocaleDateString("en-GB", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });
  }

  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleDateString("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}

function presentInvoice(row: PortalInvoice): InvoicePresentation {
  const status = (row.status || "").toUpperCase();
  const integration = (row.integrationStatus || "").toUpperCase();

  if (status === "CANCELLED") {
    return { status: "Cancelled" };
  }

  if (status === "PAID") {
    return { status: "Paid" };
  }

  if (status === "APPROVED" || status === "ACCEPTED") {
    return { status: "Approved" };
  }

  if (status === "RETURNED") {
    return {
      status: "Returned for Correction",
      issue: true,
    };
  }

  if (["ORACLE_REJECTED", "REJECTED"].includes(status)) {
    return {
      status: "Rejected",
      issue: true,
    };
  }

  if (
    ["INTEGRATION_FAILED", "FAILED"].includes(status) ||
    ["FAILED", "INTEGRATION_FAILED"].includes(integration)
  ) {
    return {
      status: "Action Required",
      issue: true,
    };
  }

  if (
    status === "PENDING" ||
    status === "UNDER_FINANCE_REVIEW"
  ) {
    return {
      status: "Pending for Approval",
    };
  }

  if (
    status === "SENT_TO_ORACLE" ||
    integration === "SUCCESS"
  ) {
    return {
      status: "Pending",
    };
  }

  if (
    ["PROCESSING", "RETRYING", "RESUBMITTED"].includes(status) ||
    ["PENDING", "PROCESSING", "RETRYING"].includes(integration)
  ) {
    return {
      status: "Processing",
    };
  }

  return {
    status: "Submitted",
  };
}

function statusClass(row: PortalInvoice) {
  const status = presentInvoice(row).status;

  if (
    [
      "Cancelled",
      "Rejected",
      "Action Required",
      "Returned for Correction",
    ].includes(status)
  ) {
    return "red";
  }

  if (status === "Processing") {
    return "orange";
  }

  if (
    [
      "Pending for Approval",
      "Approved",
      "Paid",
    ].includes(status)
  ) {
    return "green";
  }

  return "blue";
}

function canDelete(row: PortalInvoice) {
  const status = (row.status || "").toUpperCase();
  const integration = (row.integrationStatus || "").toUpperCase();

  return (
    status === "CANCELLED" ||
    [
      "INTEGRATION_FAILED",
      "FAILED",
      "ORACLE_REJECTED",
      "REJECTED",
    ].includes(status) ||
    ["FAILED", "INTEGRATION_FAILED"].includes(integration)
  );
}

export function RequestHistoryPage() {
  const [rows, setRows] = useState<PortalInvoice[]>([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const [issueLoading, setIssueLoading] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(
    null
  );

  const [detailInvoice, setDetailInvoice] =
    useState<PortalInvoice | null>(null);
  const [detailDocuments, setDetailDocuments] =
    useState<InvoiceDocumentDto[]>([]);
  const [detailLoading, setDetailLoading] =
    useState(false);
  const [detailError, setDetailError] =
    useState("");

  async function loadRows() {
    const data = await getMyPortalInvoices();
    setRows(data);
  }

  /*
   * Load invoice history immediately and then automatically
   * refresh it every 5 seconds.
   *
   * This allows Oracle/backend status changes such as:
   *
   * Processing -> Pending for Approval
   * Processing -> Rejected
   * Processing -> Paid
   *
   * to appear without manually refreshing the browser.
   */
  useEffect(() => {
    let active = true;

    const refreshRows = async () => {
      try {
        const data = await getMyPortalInvoices();

        if (active) {
          setRows(data);
        }
      } catch (error) {
        console.error(
          "Failed to refresh invoice history:",
          error
        );
      }
    };

    // Initial load
    refreshRows();

    // Automatically refresh every 5 seconds
    const intervalId = window.setInterval(
      refreshRows,
      5000
    );

    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, []);

  const statusOptions = useMemo(() => {
    const options = new Map<string, string>();

    rows.forEach((row) => {
      options.set(
        row.status,
        presentInvoice(row).status
      );
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
        formatDate(row.invoiceDate),
        row.poNumber || "-",
        Number(
          row.invoiceAmount || 0
        ).toLocaleString(),
        presentInvoice(row).status,
        row.grnNumbers?.join(", ") || "-",
        row.description || "-",
        row.remarks || "-",
      ]
        .join(" ")
        .toLowerCase();

      return (
        (!query ||
          visibleText.includes(query)) &&
        (!statusFilter ||
          row.status.toLowerCase() ===
            statusFilter.toLowerCase())
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
        "Description",
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
        row.remarks || "-",
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
        query.set(
          "issue",
          issue.reason
        );
      }

      if (issue.oracleRequestId) {
        query.set(
          "oracleRequestId",
          String(issue.oracleRequestId)
        );
      }

      if (
        issue.integrationStatus ||
        issue.status
      ) {
        query.set(
          "integrationStatus",
          issue.integrationStatus ||
            issue.status
        );
      }

      navigate(
        `/invoices/${row.id}/resubmit${
          query.toString()
            ? `?${query.toString()}`
            : ""
        }`
      );
    } finally {
      setIssueLoading(false);
    }
  }

  async function openInvoiceDetails(
    row: PortalInvoice
  ) {
    setDetailLoading(true);
    setDetailError("");
    setDetailInvoice(row);
    setDetailDocuments([]);

    try {
      const [invoice, documents] = await Promise.all([
        getPortalInvoice(row.id),
        getInvoiceDocuments(),
      ]);

      setDetailInvoice(invoice);
      setDetailDocuments(
        documents.filter(
          (document) =>
            document.invoiceId === row.id
        )
      );
    } catch (error) {
      console.error(
        "Failed to load invoice details:",
        error
      );

      setDetailError(
        "Unable to load the complete invoice details."
      );
    } finally {
      setDetailLoading(false);
    }
  }

  function closeInvoiceDetails() {
    setDetailInvoice(null);
    setDetailDocuments([]);
    setDetailError("");
  }

  /*
   * Notifications can link to /invoices?invoiceId=<portal invoice id>.
   * When that happens, automatically open the same detail popup that
   * the user gets by double-clicking an Invoice History row.
   */
  useEffect(() => {
    const invoiceId = searchParams.get("invoiceId");

    if (!invoiceId || rows.length === 0) {
      return;
    }

    const row = rows.find((item) => item.id === invoiceId);

    if (!row) {
      return;
    }

    void openInvoiceDetails(row);

    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.delete("invoiceId");
    setSearchParams(nextSearchParams, { replace: true });
  }, [rows, searchParams, setSearchParams]);

  async function deleteInvoice(
    row: PortalInvoice
  ) {
    const confirmed = window.confirm(
      row.status.toUpperCase() === "CANCELLED"
        ? `Delete cancelled invoice ${row.invoiceNumber} from the portal?`
        : `Delete failed invoice ${row.invoiceNumber} from the portal?`
    );

    if (!confirmed) {
      return;
    }

    setDeletingId(row.id);

    try {
      await deletePortalInvoice(row.id);

      setRows((current) =>
        current.filter(
          (x) => x.id !== row.id
        )
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
            Drafts, submissions, returned invoices
            and resubmission status.
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
            <th>Description</th>
            <th>Remarks</th>
            <th>GRNs</th>
            <th>Action</th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map((row) => (
            <tr
              key={row.id}
              className="invoice-history-row"
              onDoubleClick={() =>
                openInvoiceDetails(row)
              }
              title="Double-click to view complete submission details"
            >
              <td>
                {row.invoiceNumber}
              </td>

              <td>
                {formatDate(row.invoiceDate)}
              </td>

              <td>
                {row.poNumber || "-"}
              </td>

              <td>
                {Number(
                  row.invoiceAmount
                ).toLocaleString()}
              </td>

              <td>
                <span
                  className={`status ${statusClass(
                    row
                  )}`}
                >
                  {presentInvoice(row).status}
                </span>
              </td>

              <td className="invoice-history-description">
                {row.description || "-"}
              </td>

              <td>
                {row.remarks || "-"}
              </td>

              <td>
                {row.grnNumbers?.join(", ") ||
                  "-"}
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
                      onClick={() =>
                        viewIssue(row)
                      }
                    >
                      {presentInvoice(row)
                        .status === "Rejected"
                        ? "Resubmit"
                        : "View Issue"}
                    </button>
                  )}

                  {canDelete(row) && (
                    <button
                      className="danger-btn"
                      type="button"
                      disabled={
                        deletingId === row.id
                      }
                      onClick={() =>
                        deleteInvoice(row)
                      }
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
                colSpan={9}
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

      {detailInvoice && (
        <div
          className="invoice-detail-modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              closeInvoiceDetails();
            }
          }}
        >
          <section
            className="invoice-detail-modal"
            role="dialog"
            aria-modal="true"
            aria-label={`Invoice ${detailInvoice.invoiceNumber} details`}
          >
            <div className="invoice-detail-modal-head">
              <div>
                <h3>Invoice Submission Details</h3>
                <p>
                  Complete information submitted by the vendor.
                </p>
              </div>

              <button
                type="button"
                className="invoice-detail-close"
                onClick={closeInvoiceDetails}
                aria-label="Close"
              >
                ×
              </button>
            </div>

            {detailLoading && (
              <div className="invoice-detail-loading">
                Loading invoice details...
              </div>
            )}

            {detailError && (
              <div className="form-error">
                {detailError}
              </div>
            )}

            {!detailLoading && (
              <>
                <div className="invoice-detail-grid">
                  <div>
                    <span>Invoice #</span>
                    <strong>{detailInvoice.invoiceNumber}</strong>
                  </div>

                  <div>
                    <span>Invoice Date</span>
                    <strong>{formatDate(detailInvoice.invoiceDate)}</strong>
                  </div>

                  <div>
                    <span>Invoice Type</span>
                    <strong>{detailInvoice.invoiceType || "-"}</strong>
                  </div>

                  <div>
                    <span>Amount</span>
                    <strong>
                      PKR {Number(detailInvoice.invoiceAmount || 0).toLocaleString()}
                    </strong>
                  </div>

                  <div>
                    <span>Status</span>
                    <strong>{presentInvoice(detailInvoice).status}</strong>
                  </div>

                  <div>
                    <span>Integration Status</span>
                    <strong>{detailInvoice.integrationStatus || "-"}</strong>
                  </div>

                  <div className="invoice-detail-wide">
                    <span>Purchase Order(s)</span>
                    <strong>
                      {detailInvoice.poNumbers?.length
                        ? detailInvoice.poNumbers.join(", ")
                        : detailInvoice.poNumber || "-"}
                    </strong>
                  </div>

                  <div className="invoice-detail-wide">
                    <span>GRN(s)</span>
                    <strong>
                      {detailInvoice.grnNumbers?.join(", ") || "-"}
                    </strong>
                  </div>

                  <div className="invoice-detail-full">
                    <span>Description</span>
                    <strong>{detailInvoice.description || "-"}</strong>
                  </div>

                  <div className="invoice-detail-full">
                    <span>Finance Remarks</span>
                    <strong>{detailInvoice.remarks || "-"}</strong>
                  </div>
                </div>

                <div className="invoice-detail-documents">
                  <div className="invoice-detail-section-title">
                    <h4>Submitted Attachments</h4>
                    <span>
                      {detailDocuments.length} file
                      {detailDocuments.length === 1 ? "" : "s"}
                    </span>
                  </div>

                  {detailDocuments.length > 0 ? (
                    <div className="invoice-detail-document-list">
                      {detailDocuments.map((document) => (
                        <div
                          key={document.id}
                          className="invoice-detail-document"
                        >
                          <div>
                            <strong>{document.originalFileName}</strong>
                            <span>
                              {document.documentType === "INVOICE"
                                ? "Invoice Copy"
                                : document.documentType === "DELIVERY_CHALLAN"
                                  ? "Delivery Challan"
                                  : document.documentType}
                              {" · "}
                              {(document.fileSize / 1024).toFixed(1)} KB
                            </span>
                          </div>

                          <div className="invoice-detail-document-actions">
                            <button
                              type="button"
                              className="table-btn"
                              onClick={() =>
                                viewInvoiceDocument(document)
                              }
                            >
                              View
                            </button>

                            <button
                              type="button"
                              className="table-btn"
                              onClick={() =>
                                downloadInvoiceDocument(document)
                              }
                            >
                              Download
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="invoice-detail-no-docs">
                      No attachments were submitted with this invoice.
                    </div>
                  )}
                </div>
              </>
            )}
          </section>
        </div>
      )}
    </div>
  );
}