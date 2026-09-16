import { useEffect, useMemo, useState } from "react";
import {
  downloadInvoiceDocument,
  getInvoiceDocuments,
  getLiveDashboard,
  type InvoiceDocumentDto,
  viewInvoiceDocument,
} from "../api/portal";
import { DataTableToolbar } from "../components/DataTableToolbar";
import { exportRowsToCsv } from "../utils/exportCsv";

type FinanceInvoiceRow = {
  id: string;
  invoiceNumber: string;
  vendorName: string;
  invoiceDate?: string | null;
  invoiceAmount?: number | null;
  poNumbers?: string[];
  grnNumbers?: string[];
  status: string;
  rawStatus?: string;
  integrationStatus?: string;
  remarks?: string | null;
  oracleInvoiceNo?: string | null;
  updatedAt?: string | null;
};

function formatDate(value?: string | null) {
  if (!value) return "-";

  const date = new Date(value);

  return Number.isNaN(date.getTime())
    ? String(value)
    : date.toLocaleDateString("en-GB", {
        day: "2-digit",
        month: "short",
        year: "numeric",
      });
}

function statusClass(value: string) {
  const status = (value || "").toLowerCase();

  if (status.includes("reject") || status.includes("cancel")) {
    return "red";
  }

  if (status.includes("processing")) {
    return "blue";
  }

  return "green";
}

function documentLabel(documentType: string) {
  const type = (documentType || "").trim().toUpperCase();

  if (type === "INVOICE") return "Invoice Copy";
  if (type === "DELIVERY_CHALLAN") return "Delivery Challan";

  return "Supporting Document";
}

export function FinanceInvoiceRecordsPage() {
  const [rows, setRows] = useState<FinanceInvoiceRow[]>([]);
  const [documents, setDocuments] = useState<InvoiceDocumentDto[]>([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [documentActionId, setDocumentActionId] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    const load = async () => {
      try {
        const [dashboardData, documentData] = await Promise.all([
          getLiveDashboard(),
          getInvoiceDocuments(),
        ]);

        if (!active) return;

        setRows(dashboardData?.finance?.invoices || []);
        setDocuments(documentData || []);
        setError("");
      } catch (err: any) {
        if (!active) return;

        setError(
          err?.response?.data?.message ||
            err?.message ||
            "Unable to load invoice records."
        );
      } finally {
        if (active) setLoading(false);
      }
    };

    load();

    const timer = window.setInterval(load, 30_000);

    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);

  const documentsByInvoiceId = useMemo(() => {
    const map = new Map<string, InvoiceDocumentDto[]>();

    documents.forEach((document) => {
      const existing = map.get(document.invoiceId) || [];
      existing.push(document);
      map.set(document.invoiceId, existing);
    });

    map.forEach((items) => {
      items.sort((a, b) => {
        const typeOrder = (value: string) => {
          const type = (value || "").toUpperCase();
          if (type === "INVOICE") return 1;
          if (type === "DELIVERY_CHALLAN") return 2;
          return 3;
        };

        const byType = typeOrder(a.documentType) - typeOrder(b.documentType);
        if (byType !== 0) return byType;

        return a.originalFileName.localeCompare(b.originalFileName);
      });
    });

    return map;
  }, [documents]);

  const statusOptions = useMemo(() => {
    const preferred = [
      "Pending for Approval",
      "Approved",
      "Rejected",
      "Cancelled",
      "Paid",
      "Processing",
    ];

    const available = new Set(rows.map((row) => row.status).filter(Boolean));

    return preferred
      .filter((status) => available.has(status))
      .map((status) => ({ value: status, label: status }));
  }, [rows]);

  const filteredRows = useMemo(() => {
    const query = search.trim().toLowerCase();

    return rows.filter((row) => {
      const rowDocuments = documentsByInvoiceId.get(row.id) || [];

      const visibleText = [
        row.invoiceNumber,
        row.vendorName,
        row.invoiceDate || "-",
        Number(row.invoiceAmount || 0).toLocaleString(),
        row.poNumbers?.join(", ") || "-",
        row.grnNumbers?.join(", ") || "-",
        row.status,
        row.remarks || "-",
        row.oracleInvoiceNo || "-",
        rowDocuments.map((document) => document.originalFileName).join(" "),
        rowDocuments.map((document) => documentLabel(document.documentType)).join(" "),
      ]
        .join(" ")
        .toLowerCase();

      return (
        (!query || visibleText.includes(query)) &&
        (!statusFilter || row.status === statusFilter)
      );
    });
  }, [rows, search, statusFilter, documentsByInvoiceId]);

  function exportCurrentRows() {
    exportRowsToCsv(
      "finance-invoice-records.csv",
      [
        "Invoice No",
        "Vendor",
        "Invoice Date",
        "Amount (PKR)",
        "PO No(s)",
        "GRN No(s)",
        "Status",
        "Remarks",
        "Oracle Invoice No",
        "Documents",
      ],
      filteredRows.map((row) => {
        const rowDocuments = documentsByInvoiceId.get(row.id) || [];

        return [
          row.invoiceNumber,
          row.vendorName,
          row.invoiceDate || "-",
          Number(row.invoiceAmount || 0),
          row.poNumbers?.join(", ") || "-",
          row.grnNumbers?.join(", ") || "-",
          row.status,
          row.remarks || "-",
          row.oracleInvoiceNo || "-",
          rowDocuments.length
            ? rowDocuments
                .map(
                  (document) =>
                    `${documentLabel(document.documentType)}: ${document.originalFileName}`
                )
                .join(" | ")
            : "-",
        ];
      })
    );
  }

  async function handleView(document: InvoiceDocumentDto) {
    setDocumentActionId(document.id);

    try {
      await viewInvoiceDocument(document);
    } catch (err: any) {
      window.alert(
        err?.response?.data?.message ||
          err?.message ||
          "Unable to open document."
      );
    } finally {
      setDocumentActionId(null);
    }
  }

  async function handleDownload(document: InvoiceDocumentDto) {
    setDocumentActionId(document.id);

    try {
      await downloadInvoiceDocument(document);
    } catch (err: any) {
      window.alert(
        err?.response?.data?.message ||
          err?.message ||
          "Unable to download document."
      );
    } finally {
      setDocumentActionId(null);
    }
  }

  if (loading) {
    return (
      <div className="page-card">
        <p>Loading invoice records...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="page-card">
        <p className="error-message">{error}</p>
      </div>
    );
  }

  return (
    <div className="page-card">
      <div className="page-card-head invoice-history-head">
        <div>
          <h2>Invoice Records</h2>
          <p>Complete Finance/AP invoice history synchronized with Oracle EBS.</p>
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
        </div>
      </div>

      <div className="role-table-scroll">
        <table className="role-table">
          <thead>
            <tr>
              <th>Invoice No</th>
              <th>Vendor</th>
              <th>Invoice Date</th>
              <th>Amount (PKR)</th>
              <th>PO No(s)</th>
              <th>GRN No(s)</th>
              <th>Status</th>
              <th>Remarks</th>
              <th>Oracle Invoice No</th>
              <th>Documents</th>
            </tr>
          </thead>

          <tbody>
            {filteredRows.map((row) => {
              const rowDocuments = documentsByInvoiceId.get(row.id) || [];

              return (
                <tr key={row.id}>
                  <td>{row.invoiceNumber}</td>
                  <td>{row.vendorName}</td>
                  <td>{formatDate(row.invoiceDate)}</td>
                  <td>{Number(row.invoiceAmount || 0).toLocaleString()}</td>
                  <td>{row.poNumbers?.join(", ") || "-"}</td>
                  <td>{row.grnNumbers?.join(", ") || "-"}</td>
                  <td>
                    <span className={`role-status ${statusClass(row.status)}`}>
                      {row.status || "-"}
                    </span>
                  </td>
                  <td>{row.remarks || "-"}</td>
                  <td>{row.oracleInvoiceNo || "-"}</td>
                  <td style={{ minWidth: 320 }}>
                    {rowDocuments.length ? (
                      <div
                        style={{
                          display: "flex",
                          flexDirection: "column",
                          gap: 8,
                        }}
                      >
                        {rowDocuments.map((document) => (
                          <div
                            key={document.id}
                            style={{
                              display: "flex",
                              alignItems: "center",
                              gap: 6,
                              flexWrap: "wrap",
                            }}
                          >
                            <span
                              title={document.originalFileName}
                              style={{
                                minWidth: 105,
                                fontWeight: 600,
                                fontSize: 12,
                              }}
                            >
                              {documentLabel(document.documentType)}
                            </span>

                            <button
                              className="table-btn"
                              type="button"
                              disabled={documentActionId === document.id}
                              onClick={() => handleView(document)}
                            >
                              View
                            </button>

                            <button
                              className="table-btn"
                              type="button"
                              disabled={documentActionId === document.id}
                              onClick={() => handleDownload(document)}
                            >
                              Download
                            </button>
                          </div>
                        ))}
                      </div>
                    ) : (
                      "-"
                    )}
                  </td>
                </tr>
              );
            })}

            {!filteredRows.length && (
              <tr>
                <td colSpan={10} className="empty">
                  {rows.length
                    ? "No invoices match the selected filters."
                    : "No invoice records available."}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
