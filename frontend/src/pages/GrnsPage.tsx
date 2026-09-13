import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  getMyPoGrns,
} from "../api/portal";

import {
  DataTableToolbar,
} from "../components/DataTableToolbar";

import type {
  OraclePoGrn,
} from "../types";

import {
  exportRowsToCsv,
} from "../utils/exportCsv";

const label = (
  status?: string | null
) => {
  const normalized =
    (status || "")
      .trim()
      .toUpperCase();

  if (
    [
      "PENDING",
      "PENDING QC",
      "PENDING_QC",
      "AWAITING INSPECTION",
    ].includes(normalized)
  ) {
    return "Pending with QC";
  }

  if (
    normalized ===
    "NOT RECEIVED"
  ) {
    return "Not Received";
  }

  if (
    normalized ===
    "ACCEPTED"
  ) {
    return "Available";
  }

  if (
    normalized ===
    "REJECTED"
  ) {
    return "Rejected";
  }

  if (
    normalized ===
    "NOT REQUIRED" ||
    normalized ===
    "NOT_REQUIRED"
  ) {
    return "Not Required";
  }

  /*
   * IMPORTANT:
   * Do not convert NULL / blank Oracle QC status
   * into "Not Required".
   *
   * If Oracle does not return QC_STATUS or
   * INSPECTION_STATUS, show that the status is
   * unavailable instead of displaying incorrect data.
   */
  if (!normalized) {
    return "Status Not Available";
  }

  /*
   * Preserve any other actual Oracle status
   * exactly as received.
   */
  return status!.trim();
};

function statusClass(
  status?: string | null
) {
  const value =
    label(status);

  if (
    value ===
    "Pending with QC"
  ) {
    return "orange";
  }

  if (
    value ===
      "Rejected" ||
    value ===
      "Not Received"
  ) {
    return "red";
  }

  if (
    value ===
    "Status Not Available"
  ) {
    return "";
  }

  return "green";
}

function formatReceiptDate(
  value?: string | null
) {
  if (!value) {
    return "-";
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

  const day =
    String(
      date.getDate()
    ).padStart(
      2,
      "0"
    );

  const month =
    date.toLocaleString(
      "en-US",
      {
        month:
          "short",
      }
    );

  const year =
    date.getFullYear();

  return `${day}-${month}-${year}`;
}

function isWithinDateRange(
  value:
    | string
    | null
    | undefined,
  fromDate: string,
  toDate: string
) {
  if (
    !fromDate &&
    !toDate
  ) {
    return true;
  }

  if (!value) {
    return false;
  }

  const current =
    new Date(value);

  if (
    Number.isNaN(
      current.getTime()
    )
  ) {
    return false;
  }

  current.setHours(
    0,
    0,
    0,
    0
  );

  if (fromDate) {
    const from =
      new Date(
        `${fromDate}T00:00:00`
      );

    if (
      current <
      from
    ) {
      return false;
    }
  }

  if (toDate) {
    const to =
      new Date(
        `${toDate}T23:59:59`
      );

    if (
      current >
      to
    ) {
      return false;
    }
  }

  return true;
}

export function GrnsPage() {
  const [rows, setRows] =
    useState<
      OraclePoGrn[]
    >([]);

  const [search, setSearch] =
    useState("");

  const [
    statusFilter,
    setStatusFilter,
  ] =
    useState("");

  const [
    fromDate,
    setFromDate,
  ] =
    useState("");

  const [
    toDate,
    setToDate,
  ] =
    useState("");

  useEffect(() => {
    getMyPoGrns()
      .then((result) =>
        setRows(
          result.filter(
            (row) =>
              row.grnNumber
          )
        )
      );
  }, []);

  const statusOptions =
    useMemo(
      () => [
        {
          value:
            "Available",

          label:
            "Available",
        },

        {
          value:
            "Pending with QC",

          label:
            "Pending with QC",
        },

        {
          value:
            "Not Received",

          label:
            "Not Received",
        },

        {
          value:
            "Rejected",

          label:
            "Rejected",
        },

        {
          value:
            "Not Required",

          label:
            "Not Required",
        },

        {
          value:
            "Status Not Available",

          label:
            "Status Not Available",
        },
      ],
      []
    );

  const filteredRows =
    useMemo(() => {
      const query =
        search
          .trim()
          .toLowerCase();

      return rows.filter(
        (row) => {
          const qcStatus =
            label(
              row.inspectionStatus
            );

          const visibleText = [
            row.grnNumber ||
              "",
            row.poNumber,
            formatReceiptDate(
              row.receiptDate
            ),
            row.grnReceivedQuantity ??
              0,
            row.quantityAvailableToInvoice ??
              0,
            qcStatus,
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
              qcStatus
                .toLowerCase() ===
                statusFilter
                  .toLowerCase()
            )
            &&
            isWithinDateRange(
              row.receiptDate,
              fromDate,
              toDate
            )
          );
        }
      );
    }, [
      rows,
      search,
      statusFilter,
      fromDate,
      toDate,
    ]);

  function exportCurrentRows() {
    exportRowsToCsv(
      "grns.csv",
      [
        "GRN Number",
        "PO Number",
        "Receipt Date",
        "Received Qty",
        "Available Qty",
        "QC Status",
      ],
      filteredRows.map(
        (row) => [
          row.grnNumber ||
            "",
          row.poNumber,
          formatReceiptDate(
            row.receiptDate
          ),
          row.grnReceivedQuantity ??
            0,
          row.quantityAvailableToInvoice ??
            0,
          label(
            row.inspectionStatus
          ),
        ]
      )
    );
  }

  return (
    <div className="page-card">
      <div className="page-card-head vendor-table-head">
        <div>
          <h2>
            GRNs & QC Status
          </h2>

          <p>
            Available GRNs from
            Oracle EBS. Pending
            quality inspection is
            clearly highlighted.
          </p>
        </div>

        <DataTableToolbar
          searchValue={search}
          onSearchChange={
            setSearch
          }
          searchPlaceholder="Search GRNs..."
          statusValue={
            statusFilter
          }
          onStatusChange={
            setStatusFilter
          }
          statusLabel="QC Status"
          statusOptions={
            statusOptions
          }
          showDateFilter
          fromDate={
            fromDate
          }
          toDate={
            toDate
          }
          onApplyDateRange={(
            from,
            to
          ) => {
            setFromDate(
              from
            );

            setToDate(
              to
            );
          }}
          onExport={
            exportCurrentRows
          }
        />
      </div>

      <table className="data-table vendor-data-table">
        <thead>
          <tr>
            <th>
              GRN Number
            </th>

            <th>
              PO Number
            </th>

            <th>
              Receipt Date
            </th>

            <th>
              Received Qty
            </th>

            <th>
              Available Qty
            </th>

            <th>
              QC Status
            </th>
          </tr>
        </thead>

        <tbody>
          {filteredRows.map(
            (
              row,
              index
            ) => (
              <tr
                key={`${row.grnNumber}-${index}`}
              >
                <td>
                  {
                    row.grnNumber
                  }
                </td>

                <td>
                  {
                    row.poNumber
                  }
                </td>

                <td>
                  {formatReceiptDate(
                    row.receiptDate
                  )}
                </td>

                <td>
                  {
                    row.grnReceivedQuantity ||
                    0
                  }
                </td>

                <td>
                  {
                    row.quantityAvailableToInvoice ||
                    0
                  }
                </td>

                <td>
                  <span
                    className={`status ${statusClass(
                      row.inspectionStatus
                    )}`}
                  >
                    {label(
                      row.inspectionStatus
                    )}
                  </span>
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
                No GRNs match the
                selected filters.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}