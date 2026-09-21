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
  description?: string | null;
};

function formatDate(value?: string | null) {
  if (!value) return "-";

  const datePart = String(value).substring(0, 10);
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

  if (
    status.includes("reject") ||
    status.includes("cancel") ||
    status.includes("return")
  ) {
    return "red";
  }

  if (
    status.includes("processing") ||
    status.includes("pending")
  ) {
    return "blue";
  }

  return "green";
}

function documentLabel(documentType: string) {
  const type = (documentType || "")
    .trim()
    .toUpperCase();

  if (type === "INVOICE") return "Invoice Copy";
  if (type === "DELIVERY_CHALLAN") return "Delivery Challan";

  return "Supporting Document";
}

export function FinanceInvoiceRecordsPage() {
  const [rows, setRows] =
    useState<FinanceInvoiceRow[]>([]);

  const [documents, setDocuments] =
    useState<InvoiceDocumentDto[]>([]);

  const [search, setSearch] =
    useState("");

  const [statusFilter, setStatusFilter] =
    useState("");

  const [loading, setLoading] =
    useState(true);

  const [error, setError] =
    useState("");

  const [
    documentActionId,
    setDocumentActionId,
  ] = useState<string | null>(null);

  const [
    selectedInvoice,
    setSelectedInvoice,
  ] = useState<FinanceInvoiceRow | null>(null);

  useEffect(() => {
    let active = true;

    const load = async () => {
      try {
        const [
          dashboardData,
          documentData,
        ] = await Promise.all([
          getLiveDashboard(),
          getInvoiceDocuments(),
        ]);

        if (!active) {
          return;
        }

        setRows(
          dashboardData?.finance?.invoices || []
        );

        setDocuments(
          documentData || []
        );

        setError("");
      } catch (err: any) {
        if (!active) {
          return;
        }

        setError(
          err?.response?.data?.message ||
            err?.message ||
            "Unable to load invoice records."
        );
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    };

    load();

    const timer =
      window.setInterval(
        load,
        30_000
      );

    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    if (!selectedInvoice) {
      return;
    }

    const closeOnEscape = (
      event: KeyboardEvent
    ) => {
      if (event.key === "Escape") {
        setSelectedInvoice(null);
      }
    };

    window.addEventListener(
      "keydown",
      closeOnEscape
    );

    return () => {
      window.removeEventListener(
        "keydown",
        closeOnEscape
      );
    };
  }, [selectedInvoice]);

  const documentsByInvoiceId =
    useMemo(() => {
      const map =
        new Map<
          string,
          InvoiceDocumentDto[]
        >();

      documents.forEach(
        (document) => {
          const existing =
            map.get(
              document.invoiceId
            ) || [];

          existing.push(document);

          map.set(
            document.invoiceId,
            existing
          );
        }
      );

      map.forEach((items) => {
        items.sort((a, b) => {
          const typeOrder = (
            value: string
          ) => {
            const type =
              (
                value || ""
              ).toUpperCase();

            if (
              type === "INVOICE"
            ) {
              return 1;
            }

            if (
              type ===
              "DELIVERY_CHALLAN"
            ) {
              return 2;
            }

            return 3;
          };

          const byType =
            typeOrder(
              a.documentType
            ) -
            typeOrder(
              b.documentType
            );

          if (byType !== 0) {
            return byType;
          }

          return a.originalFileName.localeCompare(
            b.originalFileName
          );
        });
      });

      return map;
    }, [documents]);

  const statusOptions =
    useMemo(() => {
      const preferred = [
        "Pending for Approval",
        "Approved",
        "Rejected",
        "Cancelled",
        "Paid",
        "Processing",
      ];

      const available =
        new Set(
          rows
            .map(
              (row) =>
                row.status
            )
            .filter(Boolean)
        );

      return preferred
        .filter(
          (status) =>
            available.has(
              status
            )
        )
        .map(
          (status) => ({
            value: status,
            label: status,
          })
        );
    }, [rows]);

  const filteredRows =
    useMemo(() => {
      const query =
        search
          .trim()
          .toLowerCase();

      return rows.filter(
        (row) => {
          const rowDocuments =
            documentsByInvoiceId.get(
              row.id
            ) || [];

          const visibleText = [
            row.invoiceNumber,
            row.vendorName,
            row.invoiceDate || "-",
            Number(
              row.invoiceAmount || 0
            ).toLocaleString(),
            row.poNumbers?.join(
              ", "
            ) || "-",
            row.grnNumbers?.join(
              ", "
            ) || "-",
            row.status,
            row.rawStatus || "-",
            row.integrationStatus ||
              "-",
            row.description || "-",
            row.remarks || "-",
            row.oracleInvoiceNo ||
              "-",
            rowDocuments
              .map(
                (document) =>
                  document.originalFileName
              )
              .join(" "),
            rowDocuments
              .map(
                (document) =>
                  documentLabel(
                    document.documentType
                  )
              )
              .join(" "),
          ]
            .join(" ")
            .toLowerCase();

          return (
            (
              !query ||
              visibleText.includes(
                query
              )
            ) &&
            (
              !statusFilter ||
              row.status ===
                statusFilter
            )
          );
        }
      );
    }, [
      rows,
      search,
      statusFilter,
      documentsByInvoiceId,
    ]);

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
        "Raw Status",
        "Integration Status",
        "Description",
        "Remarks",
        "Oracle Invoice No",
        "Documents",
      ],
      filteredRows.map(
        (row) => {
          const rowDocuments =
            documentsByInvoiceId.get(
              row.id
            ) || [];

          return [
            row.invoiceNumber,
            row.vendorName,
            formatDate(
              row.invoiceDate
            ),
            Number(
              row.invoiceAmount || 0
            ),
            row.poNumbers?.join(
              ", "
            ) || "-",
            row.grnNumbers?.join(
              ", "
            ) || "-",
            row.status,
            row.rawStatus || "-",
            row.integrationStatus ||
              "-",
            row.description || "-",
            row.remarks || "-",
            row.oracleInvoiceNo ||
              "-",
            rowDocuments.length
              ? rowDocuments
                  .map(
                    (
                      document
                    ) =>
                      `${documentLabel(
                        document.documentType
                      )}: ${
                        document.originalFileName
                      }`
                  )
                  .join(" | ")
              : "-",
          ];
        }
      )
    );
  }

  async function handleView(
    document: InvoiceDocumentDto
  ) {
    setDocumentActionId(
      document.id
    );

    try {
      await viewInvoiceDocument(
        document
      );
    } catch (err: any) {
      window.alert(
        err?.response?.data
          ?.message ||
          err?.message ||
          "Unable to open document."
      );
    } finally {
      setDocumentActionId(
        null
      );
    }
  }

  async function handleDownload(
    document: InvoiceDocumentDto
  ) {
    setDocumentActionId(
      document.id
    );

    try {
      await downloadInvoiceDocument(
        document
      );
    } catch (err: any) {
      window.alert(
        err?.response?.data
          ?.message ||
          err?.message ||
          "Unable to download document."
      );
    } finally {
      setDocumentActionId(
        null
      );
    }
  }

  if (loading) {
    return (
      <div className="page-card">
        <p>
          Loading invoice
          records...
        </p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="page-card">
        <p className="error-message">
          {error}
        </p>
      </div>
    );
  }

  const selectedDocuments =
    selectedInvoice
      ? documentsByInvoiceId.get(
          selectedInvoice.id
        ) || []
      : [];

  return (
    <>
      <div className="page-card">
        <div className="page-card-head invoice-history-head">
          <div>
            <h2>
              Invoice Records
            </h2>

            <p>
              Complete Finance/AP
              invoice history
              synchronized with
              Oracle EBS. Double-click
              a row to view complete
              request details.
            </p>
          </div>

          <div className="invoice-history-actions">
            <DataTableToolbar
              searchValue={
                search
              }
              onSearchChange={
                setSearch
              }
              searchPlaceholder="Search Invoices..."
              statusValue={
                statusFilter
              }
              onStatusChange={
                setStatusFilter
              }
              statusOptions={
                statusOptions
              }
              onExport={
                exportCurrentRows
              }
            />
          </div>
        </div>

        <div className="role-table-scroll">
          <table className="role-table">
            <thead>
              <tr>
                <th>
                  Invoice No
                </th>

                <th>
                  Vendor
                </th>

                <th>
                  Invoice Date
                </th>

                <th>
                  Amount (PKR)
                </th>

                <th>
                  Status
                </th>

                <th>
                  Oracle Invoice No
                </th>
              </tr>
            </thead>

            <tbody>
              {filteredRows.map(
                (row) => (
                  <tr
                    key={row.id}
                    onDoubleClick={() =>
                      setSelectedInvoice(
                        row
                      )
                    }
                    title="Double-click to view invoice details"
                    style={{
                      cursor:
                        "pointer",
                    }}
                  >
                    <td>
                      {
                        row.invoiceNumber
                      }
                    </td>

                    <td>
                      {
                        row.vendorName
                      }
                    </td>

                    <td>
                      {formatDate(
                        row.invoiceDate
                      )}
                    </td>

                    <td>
                      {Number(
                        row.invoiceAmount ||
                          0
                      ).toLocaleString()}
                    </td>

                    <td>
                      <span
                        className={`role-status ${statusClass(
                          row.status
                        )}`}
                      >
                        {row.status ||
                          "-"}
                      </span>
                    </td>

                    <td>
                      {row.oracleInvoiceNo ||
                        "-"}
                    </td>
                  </tr>
                )
              )}

              {!filteredRows.length && (
                <tr>
                  <td
                    colSpan={6}
                    className="empty"
                  >
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

      {selectedInvoice && (
        <div
          className="invoice-detail-modal-backdrop"
          onMouseDown={(event) => {
            if (
              event.target ===
              event.currentTarget
            ) {
              setSelectedInvoice(
                null
              );
            }
          }}
          style={{
            position:
              "fixed",
            inset: 0,
            zIndex: 9999,
            background:
              "rgba(15, 23, 42, 0.42)",
            display: "flex",
            alignItems:
              "center",
            justifyContent:
              "center",
            padding: 24,
          }}
        >
          <div
            className="invoice-detail-modal"
            role="dialog"
            aria-modal="true"
            aria-label="Invoice Request Details"
            style={{
              width:
                "min(900px, 94vw)",
              maxHeight:
                "88vh",
              overflowY:
                "auto",
              background:
                "#ffffff",
              borderRadius: 14,
              boxShadow:
                "0 24px 70px rgba(15, 23, 42, 0.24)",
            }}
          >
            <div
              style={{
                display:
                  "flex",
                alignItems:
                  "flex-start",
                justifyContent:
                  "space-between",
                gap: 20,
                padding:
                  "20px 22px",
                borderBottom:
                  "1px solid #e5e7eb",
                position:
                  "sticky",
                top: 0,
                background:
                  "#ffffff",
                zIndex: 2,
              }}
            >
              <div>
                <h2
                  style={{
                    margin: 0,
                    fontSize: 20,
                  }}
                >
                  Invoice Request
                  Details
                </h2>

                <p
                  style={{
                    margin:
                      "5px 0 0",
                    color:
                      "#64748b",
                    fontSize: 13,
                  }}
                >
                  Complete vendor
                  submission and
                  Oracle status
                  details.
                </p>
              </div>

              <button
                type="button"
                onClick={() =>
                  setSelectedInvoice(
                    null
                  )
                }
                aria-label="Close"
                style={{
                  border:
                    "1px solid #d9e0dd",
                  background:
                    "#ffffff",
                  borderRadius: 8,
                  width: 36,
                  height: 36,
                  cursor:
                    "pointer",
                  fontSize: 20,
                  lineHeight: 1,
                }}
              >
                ×
              </button>
            </div>

            <div
              style={{
                padding:
                  "22px",
              }}
            >
              <div
                style={{
                  display:
                    "grid",
                  gridTemplateColumns:
                    "repeat(3, minmax(0, 1fr))",
                  gap: 14,
                }}
              >
                {[
                  [
                    "Invoice No",
                    selectedInvoice.invoiceNumber ||
                      "-",
                  ],
                  [
                    "Vendor",
                    selectedInvoice.vendorName ||
                      "-",
                  ],
                  [
                    "Invoice Date",
                    formatDate(
                      selectedInvoice.invoiceDate
                    ),
                  ],
                  [
                    "Invoice Amount",
                    `PKR ${Number(
                      selectedInvoice.invoiceAmount ||
                        0
                    ).toLocaleString()}`,
                  ],
                  [
                    "Status",
                    selectedInvoice.status ||
                      "-",
                  ],
                  [
                    "Oracle Invoice No",
                    selectedInvoice.oracleInvoiceNo ||
                      "-",
                  ],
                  [
                    "PO No(s)",
                    selectedInvoice.poNumbers?.join(
                      ", "
                    ) || "-",
                  ],
                  [
                    "GRN No(s)",
                    selectedInvoice.grnNumbers?.join(
                      ", "
                    ) || "-",
                  ],
                  [
                    "Raw Status",
                    selectedInvoice.rawStatus ||
                      "-",
                  ],
                  [
                    "Integration Status",
                    selectedInvoice.integrationStatus ||
                      "-",
                  ],
                  [
                    "Last Updated",
                    formatDate(
                      selectedInvoice.updatedAt
                    ),
                  ],
                ].map(
                  ([label, value]) => (
                    <div
                      key={label}
                      style={{
                        border:
                          "1px solid #e5e7eb",
                        borderRadius: 10,
                        padding:
                          "12px 14px",
                        minWidth: 0,
                      }}
                    >
                      <div
                        style={{
                          fontSize: 11,
                          color:
                            "#64748b",
                          marginBottom: 5,
                          fontWeight: 600,
                        }}
                      >
                        {label}
                      </div>

                      <div
                        style={{
                          fontSize: 13,
                          color:
                            "#0f172a",
                          fontWeight: 600,
                          wordBreak:
                            "break-word",
                        }}
                      >
                        {value}
                      </div>
                    </div>
                  )
                )}
              </div>

              <div
                style={{
                  display:
                    "grid",
                  gridTemplateColumns:
                    "1fr 1fr",
                  gap: 14,
                  marginTop: 14,
                }}
              >
                <div
                  style={{
                    border:
                      "1px solid #e5e7eb",
                    borderRadius: 10,
                    padding:
                      "12px 14px",
                  }}
                >
                  <div
                    style={{
                      fontSize: 11,
                      color:
                        "#64748b",
                      marginBottom: 6,
                      fontWeight: 600,
                    }}
                  >
                    Description
                  </div>

                  <div
                    style={{
                      fontSize: 13,
                      color:
                        "#0f172a",
                      whiteSpace:
                        "pre-wrap",
                    }}
                  >
                    {selectedInvoice.description ||
                      "-"}
                  </div>
                </div>

                <div
                  style={{
                    border:
                      "1px solid #e5e7eb",
                    borderRadius: 10,
                    padding:
                      "12px 14px",
                  }}
                >
                  <div
                    style={{
                      fontSize: 11,
                      color:
                        "#64748b",
                      marginBottom: 6,
                      fontWeight: 600,
                    }}
                  >
                    Remarks
                  </div>

                  <div
                    style={{
                      fontSize: 13,
                      color:
                        "#0f172a",
                      whiteSpace:
                        "pre-wrap",
                    }}
                  >
                    {selectedInvoice.remarks ||
                      "-"}
                  </div>
                </div>
              </div>

              <div
                style={{
                  marginTop: 22,
                }}
              >
                <div
                  style={{
                    display:
                      "flex",
                    alignItems:
                      "center",
                    justifyContent:
                      "space-between",
                    gap: 12,
                    marginBottom: 10,
                  }}
                >
                  <h3
                    style={{
                      margin: 0,
                      fontSize: 15,
                    }}
                  >
                    Submitted
                    Attachments
                  </h3>

                  <span
                    style={{
                      fontSize: 12,
                      color:
                        "#64748b",
                    }}
                  >
                    {
                      selectedDocuments.length
                    }{" "}
                    file
                    {selectedDocuments.length ===
                    1
                      ? ""
                      : "s"}
                  </span>
                </div>

                {selectedDocuments.length ? (
                  <div
                    style={{
                      display:
                        "flex",
                      flexDirection:
                        "column",
                      gap: 10,
                    }}
                  >
                    {selectedDocuments.map(
                      (
                        document
                      ) => (
                        <div
                          key={
                            document.id
                          }
                          style={{
                            display:
                              "flex",
                            alignItems:
                              "center",
                            justifyContent:
                              "space-between",
                            gap: 14,
                            border:
                              "1px solid #e5e7eb",
                            borderRadius: 10,
                            padding:
                              "11px 13px",
                          }}
                        >
                          <div
                            style={{
                              minWidth:
                                0,
                            }}
                          >
                            <div
                              style={{
                                fontWeight: 700,
                                fontSize: 12,
                                color:
                                  "#0f172a",
                              }}
                            >
                              {documentLabel(
                                document.documentType
                              )}
                            </div>

                            <div
                              title={
                                document.originalFileName
                              }
                              style={{
                                marginTop: 3,
                                color:
                                  "#64748b",
                                fontSize: 12,
                                overflow:
                                  "hidden",
                                textOverflow:
                                  "ellipsis",
                                whiteSpace:
                                  "nowrap",
                                maxWidth:
                                  520,
                              }}
                            >
                              {
                                document.originalFileName
                              }
                            </div>
                          </div>

                          <div
                            style={{
                              display:
                                "flex",
                              alignItems:
                                "center",
                              gap: 8,
                              flexShrink: 0,
                            }}
                          >
                            <button
                              className="table-btn"
                              type="button"
                              disabled={
                                documentActionId ===
                                document.id
                              }
                              onClick={() =>
                                handleView(
                                  document
                                )
                              }
                            >
                              View
                            </button>

                            <button
                              className="table-btn"
                              type="button"
                              disabled={
                                documentActionId ===
                                document.id
                              }
                              onClick={() =>
                                handleDownload(
                                  document
                                )
                              }
                            >
                              Download
                            </button>
                          </div>
                        </div>
                      )
                    )}
                  </div>
                ) : (
                  <div
                    style={{
                      border:
                        "1px dashed #d6dedb",
                      borderRadius: 10,
                      padding: 20,
                      textAlign:
                        "center",
                      color:
                        "#64748b",
                      fontSize: 13,
                    }}
                  >
                    No submitted
                    attachments are
                    available for this
                    invoice.
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
