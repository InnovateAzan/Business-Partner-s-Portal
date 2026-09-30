import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  downloadInvoiceDocument,
  getInvoiceDocuments,
  getMyOracleInvoices,
  getMyPoGrns,
  getMyPortalInvoices,
  viewInvoiceDocument,
} from "../api/portal";

import {
  DataTableToolbar,
} from "../components/DataTableToolbar";

import type {
  OracleInvoice,
  OraclePoGrn,
  PortalInvoice,
} from "../types";

import type {
  InvoiceDocumentDto,
} from "../api/portal";

import {
  exportRowsToCsv,
} from "../utils/exportCsv";

function invoiceKey(
  row: OracleInvoice
) {
  if (
    row.oracleInvoiceId &&
    row.oracleInvoiceId.trim()
  ) {
    return `INVOICE_ID|${row.oracleInvoiceId.trim()}`;
  }

  return [
    "FALLBACK",
    row.vendorId || "",
    row.invoiceNumber || "",
    row.invoiceDate || "",
    Number(
      row.invoiceAmount || 0
    ),
  ].join("|");
}

function uniqueInvoices(
  rows: OracleInvoice[]
) {
  const result =
    new Map<
      string,
      OracleInvoice
    >();

  for (
    const row of rows
  ) {
    const key =
      invoiceKey(row);

    const current =
      result.get(key);

    if (!current) {
      result.set(
        key,
        row
      );

      continue;
    }

    const currentHasStatus =
      Boolean(
        current.paymentStatus
      );

    const candidateHasStatus =
      Boolean(
        row.paymentStatus
      );

    if (
      candidateHasStatus &&
      !currentHasStatus
    ) {
      result.set(
        key,
        row
      );
    }
  }

  return [
    ...result.values(),
  ];
}

function formatDate(
  value?: string | null
) {
  if (!value) {
    return "-";
  }

  const datePart =
    value.substring(0, 10);

  const match =
    /^(\d{4})-(\d{2})-(\d{2})$/.exec(
      datePart
    );

  if (match) {
    const [, year, month, day] =
      match;

    const date =
      new Date(
        Number(year),
        Number(month) - 1,
        Number(day)
      );

    return date.toLocaleDateString(
      "en-GB",
      {
        day: "2-digit",
        month: "short",
        year: "numeric",
      }
    );
  }

  const date =
    new Date(value);

  if (
    Number.isNaN(
      date.getTime()
    )
  ) {
    return value;
  }

  return date.toLocaleDateString(
    "en-GB",
    {
      day: "2-digit",
      month: "short",
      year: "numeric",
    }
  );
}

function statusClass(
  value?: string | null
) {
  const status =
    (value || "")
      .trim()
      .toUpperCase();

  if (
    status.includes("PAID") ||
    status.includes("APPROV") ||
    status.includes("ACCEPT")
  ) {
    return "green";
  }

  if (
    status.includes("REJECT") ||
    status.includes("RETURN") ||
    status.includes("CANCEL")
  ) {
    return "red";
  }

  if (
    status.includes("PEND") ||
    status.includes("UNPAID")
  ) {
    return "orange";
  }

  return "blue";
}

function normalize(
  value?: string | null
) {
  return (
    value || ""
  )
    .trim()
    .toLowerCase();
}

function findPortalInvoice(
  oracleInvoice: OracleInvoice,
  portalInvoices: PortalInvoice[]
) {
  const invoiceNumber =
    normalize(
      oracleInvoice.invoiceNumber
    );

  if (!invoiceNumber) {
    return null;
  }

  const sameNumber =
    portalInvoices.filter(
      (portalInvoice) =>
        normalize(
          portalInvoice.invoiceNumber
        ) === invoiceNumber
    );

  if (
    sameNumber.length === 0
  ) {
    return null;
  }

  if (
    sameNumber.length === 1
  ) {
    return sameNumber[0];
  }

  const oraclePo =
    normalize(
      oracleInvoice.poNumber
    );

  if (oraclePo) {
    const poMatch =
      sameNumber.find(
        (portalInvoice) => {
          const portalPos =
            portalInvoice.poNumbers?.length
              ? portalInvoice.poNumbers
              : portalInvoice.poNumber
                ? [portalInvoice.poNumber]
                : [];

          return portalPos.some(
            (po) =>
              normalize(po) ===
              oraclePo
          );
        }
      );

    if (poMatch) {
      return poMatch;
    }
  }

  const oracleGrn =
    normalize(
      oracleInvoice.grnNumber
    );

  if (oracleGrn) {
    const grnMatch =
      sameNumber.find(
        (portalInvoice) =>
          (
            portalInvoice.grnNumbers ||
            []
          ).some(
            (grn) =>
              normalize(grn) ===
              oracleGrn
          )
      );

    if (grnMatch) {
      return grnMatch;
    }
  }

  return sameNumber[0];
}

export function PaymentsPage() {
  const [rows, setRows] =
    useState<OracleInvoice[]>([]);

  const [poGrnRows, setPoGrnRows] =
    useState<OraclePoGrn[]>([]);

  const [portalInvoices, setPortalInvoices] =
    useState<PortalInvoice[]>([]);

  const [documents, setDocuments] =
    useState<InvoiceDocumentDto[]>([]);

  const [search, setSearch] =
    useState("");

  const [
    statusFilter,
    setStatusFilter,
  ] =
    useState("");

  const [loading, setLoading] =
    useState(true);

  const [error, setError] =
    useState("");

  const [detailPayment, setDetailPayment] =
    useState<OracleInvoice | null>(null);

  const [detailPortalInvoice, setDetailPortalInvoice] =
    useState<PortalInvoice | null>(null);

  const [detailDocuments, setDetailDocuments] =
    useState<InvoiceDocumentDto[]>([]);

  const [documentActionId, setDocumentActionId] =
    useState<string | null>(null);

  useEffect(() => {
    let active = true;

    async function load() {
      try {
        setLoading(true);
        setError("");

        const [
          oracleRows,
          portalRows,
          invoiceDocuments,
          oraclePoGrnRows,
        ] =
          await Promise.all([
            getMyOracleInvoices(),
            getMyPortalInvoices(),
            getInvoiceDocuments(),
            getMyPoGrns(),
          ]);

        if (!active) {
          return;
        }

        setRows(
          uniqueInvoices(
            oracleRows
          )
        );

        setPortalInvoices(
          portalRows
        );

        setDocuments(
          invoiceDocuments
        );

        setPoGrnRows(
          oraclePoGrnRows
        );
      } catch (err: any) {
        if (!active) {
          return;
        }

        setError(
          err?.response?.data?.message ||
            err?.message ||
            "Unable to load payment information."
        );
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    }

    load();

    return () => {
      active = false;
    };
  }, []);

  const canonicalRows =
    useMemo(
      () =>
        uniqueInvoices(
          rows
        ),
      [rows]
    );

  const statusOptions =
    useMemo(() => {
      const values =
        new Set(
          canonicalRows.map(
            (row) =>
              row.paymentStatus ||
              "Pending"
          )
        );

      return [...values]
        .filter(Boolean)
        .sort((a, b) =>
          a.localeCompare(b)
        )
        .map((value) => ({
          value,
          label: value,
        }));
    }, [canonicalRows]);

  const filteredRows =
    useMemo(() => {
      const query =
        search
          .trim()
          .toLowerCase();

      return canonicalRows.filter(
        (row) => {
          const paymentStatus =
            row.paymentStatus ||
            "Pending";

          const portalInvoice =
            findPortalInvoice(
              row,
              portalInvoices
            );

          const visibleText = [
            row.invoiceNumber,
            row.poNumber || "-",
            row.grnNumber || "-",
            row.invoiceAmount ?? 0,
            row.amountPaid ?? 0,
            row.outstandingAmount ?? 0,
            paymentStatus,
            row.approvalStatus ||
              "-",
            portalInvoice?.description ||
              "-",
            portalInvoice?.remarks ||
              "-",
          ]
            .join(" ")
            .toLowerCase();

          return (
            (
              !query ||
              visibleText.includes(
                query
              )
            )
            &&
            (
              !statusFilter ||
              paymentStatus
                .toLowerCase() ===
                statusFilter
                  .toLowerCase()
            )
          );
        }
      );
    }, [
      canonicalRows,
      portalInvoices,
      search,
      statusFilter,
    ]);

  function exportPayments() {
    exportRowsToCsv(
      "payments.csv",
      [
        "Invoice #",
        "PO Number",
        "GRN Number",
        "Invoice Amount",
        "Amount Paid",
        "Outstanding",
        "Payment Status",
        "Approval Status",
      ],
      filteredRows.map(
        (row) => [
          row.invoiceNumber,
          row.poNumber || "-",
          row.grnNumber || "-",
          Number(
            row.invoiceAmount || 0
          ),
          Number(
            row.amountPaid || 0
          ),
          Number(
            row.outstandingAmount ||
              0
          ),
          row.paymentStatus ||
            "Pending",
          row.approvalStatus ||
            "-",
        ]
      )
    );
  }

  function openPaymentDetails(
    row: OracleInvoice
  ) {
    const portalInvoice =
      findPortalInvoice(
        row,
        portalInvoices
      );

    setDetailPayment(row);
    setDetailPortalInvoice(
      portalInvoice
    );

    setDetailDocuments(
      portalInvoice
        ? documents.filter(
            (document) =>
              document.invoiceId ===
              portalInvoice.id
          )
        : []
    );
  }

  function closePaymentDetails() {
    setDetailPayment(null);
    setDetailPortalInvoice(null);
    setDetailDocuments([]);
    setDocumentActionId(null);
  }

  async function handleViewDocument(
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
        err?.response?.data?.message ||
          err?.message ||
          "Unable to open document."
      );
    } finally {
      setDocumentActionId(null);
    }
  }

  async function handleDownloadDocument(
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
        err?.response?.data?.message ||
          err?.message ||
          "Unable to download document."
      );
    } finally {
      setDocumentActionId(null);
    }
  }

  const detailPoNumbers =
    detailPortalInvoice?.poNumbers?.length
      ? detailPortalInvoice.poNumbers
      : detailPortalInvoice?.poNumber
        ? [detailPortalInvoice.poNumber]
        : detailPayment?.poNumber
          ? [detailPayment.poNumber]
          : [];

  const detailGrnNumbers =
    detailPortalInvoice?.grnNumbers?.length
      ? detailPortalInvoice.grnNumbers
      : detailPayment?.grnNumber
        ? [detailPayment.grnNumber]
        : [];

  const relatedPoGrnRows =
    detailPayment
      ? poGrnRows.filter(
          (row) => {
            const poMatches =
              detailPoNumbers.length === 0
              || detailPoNumbers.some(
                (po) =>
                  normalize(po) ===
                  normalize(row.poNumber)
              );

            const grnMatches =
              detailGrnNumbers.length === 0
              || detailGrnNumbers.some(
                (grn) =>
                  normalize(grn) ===
                  normalize(row.grnNumber)
              );

            return poMatches && grnMatches;
          }
        )
      : [];

  const detailPrNumbers =
    [
      ...new Set(
        relatedPoGrnRows
          .map(
            (row) =>
              row.prNumber?.trim()
          )
          .filter(
            (value): value is string =>
              Boolean(value)
          )
      ),
    ];

  const detailGrnDates =
    [
      ...new Set(
        relatedPoGrnRows
          .map(
            (row) =>
              row.receiptDate
          )
          .filter(
            (value): value is string =>
              Boolean(value)
          )
      ),
    ];

  return (
    <div className="page-card">
      <div className="page-card-head vendor-table-head">
        <div>
          <h2>
            Payments
          </h2>

          <p>
            Read-only payment status synchronized from Oracle EBS.
          </p>
        </div>

        <DataTableToolbar
          searchValue={search}
          onSearchChange={setSearch}
          searchPlaceholder="Search Payments..."
          statusValue={statusFilter}
          onStatusChange={setStatusFilter}
          statusLabel="Payment Status"
          statusOptions={statusOptions}
          onExport={exportPayments}
        />
      </div>

      {error && (
        <div className="form-error">
          {error}
        </div>
      )}

      <table className="data-table vendor-data-table payment-table">
        <thead>
          <tr>
            <th>Invoice #</th>
            <th>PO Number</th>
            <th>GRN Number</th>
            <th className="payment-column">
              Invoice Amount
            </th>
            <th className="payment-column">
              Amount Paid
            </th>
            <th className="payment-column">
              Outstanding
            </th>
            <th>Payment Status</th>
            <th>Approval Status</th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map(
            (row) => (
              <tr
                key={invoiceKey(row)}
                className="invoice-history-row"
                onDoubleClick={() =>
                  openPaymentDetails(row)
                }
                title="Double-click to view payment and invoice submission details"
              >
                <td>
                  {row.invoiceNumber}
                </td>

                <td>
                  {row.poNumber || "-"}
                </td>

                <td>
                  {row.grnNumber || "-"}
                </td>

                <td className="payment-column">
                  {Number(
                    row.invoiceAmount || 0
                  ).toLocaleString()}
                </td>

                <td className="payment-column">
                  {Number(
                    row.amountPaid || 0
                  ).toLocaleString()}
                </td>

                <td className="payment-column">
                  {Number(
                    row.outstandingAmount || 0
                  ).toLocaleString()}
                </td>

                <td>
                  <span
                    className={`status ${statusClass(
                      row.paymentStatus ||
                        "Pending"
                    )}`}
                  >
                    {row.paymentStatus ||
                      "Pending"}
                  </span>
                </td>

                <td>
                  {row.approvalStatus ||
                    "-"}
                </td>
              </tr>
            )
          )}

          {!loading &&
            !filteredRows.length && (
              <tr>
                <td
                  colSpan={8}
                  className="empty"
                >
                  No payments match the selected filters.
                </td>
              </tr>
            )}

          {loading && (
            <tr>
              <td
                colSpan={8}
                className="empty"
              >
                Loading payments...
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {detailPayment && (
        <div
          className="invoice-detail-modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (
              event.target ===
              event.currentTarget
            ) {
              closePaymentDetails();
            }
          }}
        >
          <section
            className="invoice-detail-modal"
            role="dialog"
            aria-modal="true"
            aria-label={`Invoice ${detailPayment.invoiceNumber} payment details`}
          >
            <div className="invoice-detail-modal-head">
              <div>
                <h3>
                  Payment & Invoice Details
                </h3>

                <p>
                  Oracle payment status with the vendor&apos;s original portal submission.
                </p>
              </div>

              <button
                type="button"
                className="invoice-detail-close"
                onClick={closePaymentDetails}
                aria-label="Close"
              >
                ×
              </button>
            </div>

            <div className="invoice-detail-grid">
              <div>
                <span>Invoice #</span>
                <strong>
                  {detailPayment.invoiceNumber}
                </strong>
              </div>

              <div>
                <span>Invoice Date</span>
                <strong>
                  {formatDate(
                    detailPayment.invoiceDate
                  )}
                </strong>
              </div>

              <div>
                <span>Invoice Type</span>
                <strong>
                  {detailPortalInvoice?.invoiceType ||
                    "-"}
                </strong>
              </div>

              <div>
                <span>Invoice Amount</span>
                <strong>
                  PKR{" "}
                  {Number(
                    detailPayment.invoiceAmount ||
                      0
                  ).toLocaleString()}
                </strong>
              </div>

              <div>
                <span>Amount Paid</span>
                <strong>
                  PKR{" "}
                  {Number(
                    detailPayment.amountPaid ||
                      0
                  ).toLocaleString()}
                </strong>
              </div>

              <div>
                <span>Outstanding</span>
                <strong>
                  PKR{" "}
                  {Number(
                    detailPayment.outstandingAmount ||
                      0
                  ).toLocaleString()}
                </strong>
              </div>

              <div>
                <span>Payment Status</span>
                <strong>
                  {detailPayment.paymentStatus ||
                    "Pending"}
                </strong>
              </div>

              <div>
                <span>Approval Status</span>
                <strong>
                  {detailPayment.approvalStatus ||
                    "-"}
                </strong>
              </div>

              <div className="invoice-detail-wide">
                <span>
                  Purchase Order(s)
                </span>

                <strong>
                  {detailPortalInvoice?.poNumbers?.length
                    ? detailPortalInvoice.poNumbers.join(", ")
                    : detailPortalInvoice?.poNumber ||
                      detailPayment.poNumber ||
                      "-"}
                </strong>
              </div>

              <div className="invoice-detail-wide">
                <span>GRN(s)</span>

                <strong>
                  {detailPortalInvoice?.grnNumbers?.length
                    ? detailPortalInvoice.grnNumbers.join(", ")
                    : detailPayment.grnNumber ||
                      "-"}
                </strong>
              </div>

              {/* NEW */}
              <div className="invoice-detail-wide">
                <span>
                  PR Number(s)
                </span>

                <strong>
                  {detailPrNumbers.length
                    ? detailPrNumbers.join(", ")
                    : "-"}
                </strong>
              </div>

              {/* NEW */}
              <div className="invoice-detail-wide">
                <span>
                  GRN Date(s)
                </span>

                <strong>
                  {detailGrnDates.length
                    ? detailGrnDates
                        .map(
                          (value) =>
                            formatDate(value)
                        )
                        .join(", ")
                    : formatDate(
                        detailPayment.receiptDate
                      )}
                </strong>
              </div>

              <div className="invoice-detail-full">
                <span>Description</span>

                <strong>
                  {detailPortalInvoice?.description ||
                    "-"}
                </strong>
              </div>

              <div className="invoice-detail-full">
                <span>
                  Finance Remarks
                </span>

                <strong>
                  {detailPortalInvoice?.remarks ||
                    "-"}
                </strong>
              </div>
            </div>

            <div className="invoice-detail-documents">
              <div className="invoice-detail-section-title">
                <h4>
                  Submitted Attachments
                </h4>

                <span>
                  {detailDocuments.length} file
                  {detailDocuments.length === 1
                    ? ""
                    : "s"}
                </span>
              </div>

              {detailPortalInvoice ? (
                detailDocuments.length > 0 ? (
                  <div className="invoice-detail-document-list">
                    {detailDocuments.map(
                      (document) => (
                        <div
                          key={document.id}
                          className="invoice-detail-document"
                        >
                          <div>
                            <strong>
                              {document.originalFileName}
                            </strong>

                            <span>
                              {document.documentType ===
                              "INVOICE"
                                ? "Invoice Copy"
                                : document.documentType ===
                                    "DELIVERY_CHALLAN"
                                  ? "Delivery Challan"
                                  : document.documentType}
                              {" · "}
                              {(
                                document.fileSize /
                                1024
                              ).toFixed(1)}{" "}
                              KB
                            </span>
                          </div>

                          <div className="invoice-detail-document-actions">
                            <button
                              type="button"
                              className="table-btn"
                              disabled={
                                documentActionId ===
                                document.id
                              }
                              onClick={() =>
                                handleViewDocument(
                                  document
                                )
                              }
                            >
                              View
                            </button>

                            <button
                              type="button"
                              className="table-btn"
                              disabled={
                                documentActionId ===
                                document.id
                              }
                              onClick={() =>
                                handleDownloadDocument(
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
                  <div className="invoice-detail-no-docs">
                    No attachments were submitted with this invoice.
                  </div>
                )
              ) : (
                <div className="invoice-detail-no-docs">
                  This Oracle invoice does not have a matching portal submission, so portal attachments are not available.
                </div>
              )}
            </div>
          </section>
        </div>
      )}
    </div>
  );
}