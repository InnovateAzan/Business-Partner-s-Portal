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
      .then(setRows);
  }, []);

  const statusOptions =
    useMemo(() => {
      const values =
        new Set(
          rows.map(
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
    }, [rows]);

  const filteredRows =
    useMemo(() => {
      const query =
        search
          .trim()
          .toLowerCase();

      return rows.filter(
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
      rows,
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
            (row, index) => (
              <tr
                key={`${row.invoiceNumber}-${index}`}
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