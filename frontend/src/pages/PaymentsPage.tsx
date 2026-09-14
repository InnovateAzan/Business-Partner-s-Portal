import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  getMyOracleInvoices,
} from "../api/portal";

import {
  DataTableToolbar,
} from "../components/DataTableToolbar";

import type {
  OracleInvoice,
} from "../types";

import {
  exportRowsToCsv,
} from "../utils/exportCsv";

// ============================================================
// PAYMENT / INVOICE DEDUPLICATION
// ============================================================

function invoiceKey(
  row: OracleInvoice
) {
  /*
   * Oracle INVOICE_ID is the real invoice-header identifier and is
   * therefore the safest key for the Payments page.
   *
   * The fallback only protects the UI if a legacy/custom Oracle view
   * unexpectedly omits INVOICE_ID.
   */
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

    /*
     * Backend already returns one row per invoice. This is a defensive
     * frontend guard so a future Oracle-view change cannot duplicate the
     * same invoice on screen or in CSV export.
     */
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

export function PaymentsPage() {
  const [rows, setRows] =
    useState<OracleInvoice[]>([]);

  const [search, setSearch] =
    useState("");

  const [
    statusFilter,
    setStatusFilter,
  ] =
    useState("");

  useEffect(() => {
    getMyOracleInvoices()
      .then(
        (data) =>
          setRows(
            uniqueInvoices(
              data
            )
          )
      );
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

          const visibleText = [
            row.invoiceNumber,
            row.invoiceAmount ?? 0,
            row.amountPaid ?? 0,
            row.outstandingAmount ?? 0,
            paymentStatus,
            row.approvalStatus ||
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
      search,
      statusFilter,
    ]);

  function exportPayments() {
    exportRowsToCsv(
      "payments.csv",
      [
        "Invoice #",
        "Invoice Amount",
        "Amount Paid",
        "Outstanding",
        "Payment Status",
        "Approval Status",
      ],
      filteredRows.map(
        (row) => [
          row.invoiceNumber,
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

  return (
    <div className="page-card">
      <div className="page-card-head vendor-table-head">
        <div>
          <h2>
            Payments
          </h2>

          <p>
            Read-only payment status
            synchronized from Oracle
            EBS.
          </p>
        </div>

        <DataTableToolbar
          searchValue={search}
          onSearchChange={
            setSearch
          }
          searchPlaceholder="Search Payments..."
          statusValue={
            statusFilter
          }
          onStatusChange={
            setStatusFilter
          }
          statusLabel="Payment Status"
          statusOptions={
            statusOptions
          }
          onExport={
            exportPayments
          }
        />
      </div>

      <table className="data-table vendor-data-table payment-table">
        <thead>
          <tr>
            <th>
              Invoice #
            </th>

            <th className="payment-column">
              Invoice Amount
            </th>

            <th className="payment-column">
              Amount Paid
            </th>

            <th className="payment-column">
              Outstanding
            </th>

            <th>
              Payment Status
            </th>

            <th>
              Approval Status
            </th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map(
            (row) => (
              <tr
                key={
                  invoiceKey(
                    row
                  )
                }
              >
                <td>
                  {
                    row.invoiceNumber
                  }
                </td>

                <td className="payment-column">
                  {Number(
                    row.invoiceAmount ||
                      0
                  ).toLocaleString()}
                </td>

                <td className="payment-column">
                  {Number(
                    row.amountPaid ||
                      0
                  ).toLocaleString()}
                </td>

                <td className="payment-column">
                  {Number(
                    row.outstandingAmount ||
                      0
                  ).toLocaleString()}
                </td>

                <td>
                  <span className="status green">
                    {
                      row.paymentStatus ||
                      "Pending"
                    }
                  </span>
                </td>

                <td>
                  {
                    row.approvalStatus ||
                    "-"
                  }
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
                No payments match
                the selected filters.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
