import {
  Fragment,
  useEffect,
  useMemo,
  useState
} from "react";

import { useNavigate } from "react-router-dom";

import {
  getMyPoGrns,
  getMyPortalInvoices
} from "../api/portal";

import { DataTableToolbar } from "../components/DataTableToolbar";

import type {
  OraclePoGrn,
  PortalInvoice
} from "../types";

import { exportRowsToCsv } from "../utils/exportCsv";


const money = (value: number) =>
  value.toLocaleString(
    undefined,
    {
      maximumFractionDigits: 2
    }
  );


const qty = (value: number) =>
  value.toLocaleString(
    undefined,
    {
      maximumFractionDigits: 3
    }
  );


const date = (
  value?: string | null
) =>
  value
    ? new Date(value)
        .toLocaleDateString(
          "en-GB",
          {
            day: "2-digit",
            month: "short",
            year: "numeric"
          }
        )
    : "-";


const pendingQc = (
  value?: string | null
) =>
  [
    "PENDING",
    "PENDING QC",
    "PENDING_QC",
    "AWAITING INSPECTION",
    "NOT RECEIVED"
  ]
    .includes(
      (value || "")
        .trim()
        .toUpperCase()
    );


const hasAvailable = (
  row: OraclePoGrn
) =>
  !pendingQc(
    row.inspectionStatus
  )
  &&
  Number(
    row.quantityAvailableToInvoice
    || 0
  ) > 0;


const qcLabel = (
  value?: string | null
) =>
{
  if (pendingQc(value))
  {
    return "Pending with QC";
  }

  if (
    (value || "")
      .trim()
      .toUpperCase()
    === "ACCEPTED"
  )
  {
    return "Available";
  }

  return (
    value?.trim()
    || "Status Not Available"
  );
};


const qcClass = (
  value: string
) =>
{
  if (value === "Available")
  {
    return "green";
  }

  if (value === "Pending with QC")
  {
    return "orange";
  }

  if (
    /reject|not received/i
      .test(value)
  )
  {
    return "red";
  }

  return "blue";
};


const presentInvoiceStatus = (
  row: PortalInvoice
) =>
{
  const status =
    (row.status || "")
      .trim()
      .toUpperCase();

  const integration =
    (row.integrationStatus || "")
      .trim()
      .toUpperCase();

  if (status === "CANCELLED")
  {
    return "Cancelled";
  }

  if (status === "PAID")
  {
    return "Pending for Payment";
  }

  if (
    status === "APPROVED"
    || status === "ACCEPTED"
  )
  {
    return "Approved";
  }

  if (status === "RETURNED")
  {
    return "Returned for Correction";
  }

  if (
    [
      "INTEGRATION_FAILED",
      "FAILED",
      "ORACLE_REJECTED",
      "REJECTED"
    ].includes(status)
    || [
      "FAILED",
      "INTEGRATION_FAILED"
    ].includes(integration)
  )
  {
    return "Action Required";
  }

  if (
    status === "PENDING"
    || status === "UNDER_FINANCE_REVIEW"
    || status === "SENT_TO_ORACLE"
    || integration === "SUCCESS"
  )
  {
    return "Pending";
  }

  if (
    [
      "PROCESSING",
      "RETRYING",
      "RESUBMITTED"
    ].includes(status)
    || [
      "PENDING",
      "PROCESSING",
      "RETRYING"
    ].includes(integration)
  )
  {
    return "Processing";
  }

  return "Submitted";
};


const invoiceStatusClass = (
  value: string
) =>
{
  if (
    [
      "Cancelled",
      "Action Required",
      "Returned for Correction"
    ].includes(value)
  )
  {
    return "red";
  }

  if (
    [
      "Pending",
      "Processing"
    ].includes(value)
  )
  {
    return "orange";
  }

  if (
    [
      "Approved",
      "Pending for Payment"
    ].includes(value)
  )
  {
    return "green";
  }

  return "blue";
};


const invoiceForGrn = (
  invoice: PortalInvoice,
  grnNumber: string
) =>
  (invoice.grnNumbers || [])
    .map(
      value =>
        String(value).trim()
    )
    .includes(
      grnNumber.trim()
    );


// ============================================================
// UNIQUE GRN BUSINESS-LINE KEY
// ============================================================
//
// IMPORTANT:
//
// RCV_TRANSACTION_ID is NOT used as the primary display key.
//
// Oracle can return several transaction rows for the same
// physical receipt / shipment line.
//
// One visible line in the portal should represent:
//
//     GRN_NUMBER + SHIPMENT_LINE_ID
//
// This stops received qty, available qty and amount from being
// counted multiple times.
// ============================================================

const receiptKey = (
  row: OraclePoGrn
) =>
{
  const grn =
    row.grnNumber?.trim()
    || "NO_GRN";

  /*
   * Best key:
   *
   * Different shipmentLineId means genuinely different
   * Oracle GRN lines.
   */
  if (
    row.shipmentLineId?.trim()
  )
  {
    return (
      `GRN|${grn}`
      + `|SHIPMENT_LINE|`
      + row.shipmentLineId.trim()
    );
  }

  /*
   * Fallback when shipment line ID is unavailable.
   */
  if (
    row.poLineId?.trim()
  )
  {
    return [
      "GRN",
      grn,
      "PO_LINE",
      row.poLineId.trim(),

      row.itemId?.trim()
      || row.itemCode?.trim()
      || "NO_ITEM",

      Number(
        row.grnReceivedQuantity
        ?? row.receivedQuantity
        ?? 0
      ),

      Number(
        row.unitPrice
        ?? 0
      )

    ].join("|");
  }

  /*
   * Final fallback.
   */
  return [
    "GRN",
    grn,

    "FALLBACK",

    row.poLineNum?.trim()
    || "NO_LINE",

    row.itemId?.trim()
    || row.itemCode?.trim()
    || "NO_ITEM",

    Number(
      row.grnReceivedQuantity
      ?? row.receivedQuantity
      ?? 0
    ),

    Number(
      row.unitPrice
      ?? 0
    ),

    row.receiptDate
    || "NO_DATE"

  ].join("|");
};


// ============================================================
// DEDUPLICATE GRN LINES
// ============================================================

const unique = (
  rows: OraclePoGrn[]
) =>
{
  const byBusinessLine =
    new Map<
      string,
      OraclePoGrn
    >();

  for (
    const row of rows
  )
  {
    const key =
      receiptKey(row);

    const current =
      byBusinessLine.get(key);

    /*
     * First occurrence.
     */
    if (!current)
    {
      byBusinessLine.set(
        key,
        row
      );

      continue;
    }

    /*
     * Same logical GRN line appeared again.
     *
     * Prefer whichever row reports the greater
     * current available quantity.
     */
    const currentAvailable =
      Number(
        current
          .quantityAvailableToInvoice
        || 0
      );

    const candidateAvailable =
      Number(
        row.quantityAvailableToInvoice
        || 0
      );

    if (
      candidateAvailable
      >
      currentAvailable
    )
    {
      byBusinessLine.set(
        key,
        row
      );
    }
  }

  return [
    ...byBusinessLine.values()
  ];
};


type PoSummary =
{
  poNumber: string;

  rows: OraclePoGrn[];

  poDate?:
    string
    | null;

  status: string;

  currency: string;

  poAmount: number;

  invoiced: number;

  open: number;

  availableAmount: number;

  totalGrns: number;

  availableGrns: number;

  availableLines: number;

  qcStatus: string;
};


type GrnSummary =
{
  number: string;

  rows: OraclePoGrn[];

  received: number;

  available: number;

  amount: number;

  availableAmount: number;

  status: string;
};


function invoiceForPo(
  invoice: PortalInvoice,
  poNumber: string
)
{
  const poNumbers =
    invoice.poNumbers?.length
      ?
      invoice.poNumbers
      :
      (
        invoice.poNumber
        || ""
      )
        .split(",")
        .map(
          value =>
            value.trim()
        );

  return (
    poNumbers.includes(
      poNumber
    )
    &&
    ![
      "DRAFT",
      "CANCELLED",
      "REJECTED"
    ]
      .includes(
        (
          invoice.status
          || ""
        )
          .toUpperCase()
      )
  );
}


// ============================================================
// BUILD GRN SUMMARY
// ============================================================

function toGrns(
  rows: OraclePoGrn[]
):
  GrnSummary[]
{
  const groups =
    new Map<
      string,
      OraclePoGrn[]
    >();

  /*
   * Defensive dedupe is performed BEFORE
   * grouping and calculating totals.
   *
   * This is important because otherwise even
   * hidden duplicates would inflate amounts.
   */
  unique(
    rows.filter(
      row =>
        row.grnNumber
    )
  )
    .forEach(
      row =>
      {
        const grnNumber =
          row.grnNumber!;

        groups.set(
          grnNumber,
          [
            ...(
              groups.get(
                grnNumber
              )
              || []
            ),
            row
          ]
        );
      }
    );

  return [
    ...groups
  ]
    .map(
      (
        [
          number,
          grnRows
        ]
      ) =>
      {
        const availableRows =
          grnRows.filter(
            hasAvailable
          );

        const rawStatus =
          availableRows.length
            ?
            "Available"
            :
            grnRows
              .map(
                row =>
                  row.inspectionStatus
              )
              .find(
                pendingQc
              )
            ||
            grnRows[0]
              .inspectionStatus;

        const received =
          grnRows.reduce(
            (
              sum,
              row
            ) =>
              sum
              +
              Number(
                row.grnReceivedQuantity
                ??
                row.receivedQuantity
                ??
                0
              ),
            0
          );

        const available =
          grnRows.reduce(
            (
              sum,
              row
            ) =>
              sum
              +
              Number(
                row.quantityAvailableToInvoice
                || 0
              ),
            0
          );

        const amount =
          grnRows.reduce(
            (
              sum,
              row
            ) =>
              sum
              +
              (
                Number(
                  row.grnReceivedQuantity
                  ??
                  row.receivedQuantity
                  ??
                  0
                )
                *
                Number(
                  row.unitPrice
                  || 0
                )
              ),
            0
          );

        const availableAmount =
          availableRows.reduce(
            (
              sum,
              row
            ) =>
              sum
              +
              (
                Number(
                  row.quantityAvailableToInvoice
                  || 0
                )
                *
                Number(
                  row.unitPrice
                  || 0
                )
              ),
            0
          );

        return {
          number,
          rows: grnRows,
          received,
          available,
          amount,
          availableAmount,
          status:
            qcLabel(
              rawStatus
            )
        };
      }
    );
}


// ============================================================
// PAGE
// ============================================================

export function PurchaseOrdersPage()
{
  const navigate =
    useNavigate();


  const [
    rows,
    setRows
  ] =
    useState<
      OraclePoGrn[]
    >([]);


  const [
    invoices,
    setInvoices
  ] =
    useState<
      PortalInvoice[]
    >([]);


  const [
    search,
    setSearch
  ] =
    useState("");


  const [
    status,
    setStatus
  ] =
    useState("");


  const [
    from,
    setFrom
  ] =
    useState("");


  const [
    to,
    setTo
  ] =
    useState("");


  const [
    page,
    setPage
  ] =
    useState(1);


  const [
    selected,
    setSelected
  ] =
    useState<
      PoSummary
      | null
    >(null);


  const [
    grnView,
    setGrnView
  ] =
    useState<
      "available"
      | "all"
    >(
      "available"
    );


  const [
    expanded,
    setExpanded
  ] =
    useState<
      Set<string>
    >(
      new Set()
    );


  // ============================================================
  // LOAD DATA
  // ============================================================

  useEffect(
    () =>
    {
      Promise.all(
        [
          getMyPoGrns(),
          getMyPortalInvoices()
        ]
      )
        .then(
          (
            [
              poRows,
              portalInvoices
            ]
          ) =>
          {
            /*
             * Store already-deduplicated rows as another
             * defensive layer.
             */
            setRows(
              unique(
                poRows
              )
            );

            setInvoices(
              portalInvoices
            );
          }
        );
    },
    []
  );


  // ============================================================
  // PO SUMMARIES
  // ============================================================

  const summaries =
    useMemo<
      PoSummary[]
    >(
      () =>
      {
        const byPo =
          new Map<
            string,
            OraclePoGrn[]
          >();

        /*
         * Always work with canonical rows.
         */
        const canonicalRows =
          unique(rows);

        canonicalRows.forEach(
          row =>
          {
            byPo.set(
              row.poNumber,
              [
                ...(
                  byPo.get(
                    row.poNumber
                  )
                  || []
                ),
                row
              ]
            );
          }
        );

        return [
          ...byPo
        ]
          .map(
            (
              [
                poNumber,
                poRows
              ]
            ) =>
            {
              const canonical =
                unique(
                  poRows
                );


              /*
               * PO amount must be calculated once per
               * actual PO line.
               */
              const poLines =
                [
                  ...new Map(
                    canonical.map(
                      row =>
                        [
                          row.poLineId
                          ||
                          row.poLineNum
                          ||
                          `${row.itemCode}-${row.poLineAmount}`,

                          row
                        ]
                    )
                  )
                    .values()
                ];


              const grns =
                toGrns(
                  canonical
                );


              const availableRows =
                canonical.filter(
                  hasAvailable
                );


              const poAmount =
                poLines.reduce(
                  (
                    sum,
                    row
                  ) =>
                    sum
                    +
                    Number(
                      row.poLineAmount
                      || 0
                    ),
                  0
                );


              const invoiced =
                invoices
                  .filter(
                    invoice =>
                      invoiceForPo(
                        invoice,
                        poNumber
                      )
                  )
                  .reduce(
                    (
                      sum,
                      invoice
                    ) =>
                      sum
                      +
                      Number(
                        invoice.invoiceAmount
                        || 0
                      ),
                    0
                  );


              /*
               * Since availableRows is now canonical,
               * duplicate Oracle transaction rows can
               * no longer inflate this amount.
               */
              const availableAmount =
                availableRows.reduce(
                  (
                    sum,
                    row
                  ) =>
                    sum
                    +
                    (
                      Number(
                        row.quantityAvailableToInvoice
                        || 0
                      )
                      *
                      Number(
                        row.unitPrice
                        || 0
                      )
                    ),
                  0
                );


              return {
                poNumber,

                rows:
                  canonical,

                poDate:
                  canonical[0]
                    ?.poCreationDate
                  ||
                  canonical[0]
                    ?.poApprovedDate
                  ||
                  canonical[0]
                    ?.receiptDate,

                status:
                  canonical[0]
                    ?.poStatus
                  ||
                  "Open",

                currency:
                  canonical[0]
                    ?.currencyCode
                  ||
                  "PKR",

                poAmount,

                invoiced,

                open:
                  Math.max(
                    poAmount
                    -
                    invoiced,
                    0
                  ),

                availableAmount,

                totalGrns:
                  grns.length,

                availableGrns:
                  grns
                    .filter(
                      grn =>
                        grn.rows
                          .some(
                            hasAvailable
                          )
                    )
                    .length,

                availableLines:
                  availableRows.length,

                qcStatus:
                  availableRows.length
                    ?
                    "Available"
                    :
                    canonical
                      .map(
                        row =>
                          row.inspectionStatus
                      )
                      .find(
                        pendingQc
                      )
                      ?
                      "Pending with QC"
                      :
                      qcLabel(
                        canonical[0]
                          ?.inspectionStatus
                      )
              };
            }
          );
      },
      [
        rows,
        invoices
      ]
    );


  // ============================================================
  // QC STATUS FILTER
  // ============================================================

  const statusOptions =
    useMemo(
      () =>
        [
          ...new Set(
            summaries.map(
              row =>
                row.qcStatus
            )
          )
        ]
          .sort()
          .map(
            value =>
              ({
                value,
                label: value
              })
          ),
      [
        summaries
      ]
    );


  // ============================================================
  // FILTERS
  // ============================================================

  const filtered =
    useMemo(
      () =>
        summaries.filter(
          row =>
          {
            const d =
              row.poDate
                ?
                new Date(
                  row.poDate
                )
                :
                null;

            const searchValue =
              search
                .trim()
                .toLowerCase();

            const matchesSearch =
              !searchValue
              ||
              [
                row.poNumber,
                row.status,
                row.qcStatus
              ]
                .join(" ")
                .toLowerCase()
                .includes(
                  searchValue
                );

            const matchesStatus =
              !status
              ||
              row.qcStatus
                .toLowerCase()
              ===
              status
                .toLowerCase();

            const fromStart =
              from
                ? new Date(`${from}T00:00:00`)
                : null;

            const fromEnd =
              from
                ? new Date(`${from}T23:59:59.999`)
                : null;

            const toEnd =
              to
                ? new Date(`${to}T23:59:59.999`)
                : null;

            /*
             * Date filter behavior:
             *
             * From only  -> exact selected date
             * To only    -> all records up to selected date
             * From + To  -> inclusive date range
             */
            const matchesDate =
              !from && !to
                ? true
                : from && !to
                  ? Boolean(
                      d
                      &&
                      fromStart
                      &&
                      fromEnd
                      &&
                      d >= fromStart
                      &&
                      d <= fromEnd
                    )
                  : !from && to
                    ? Boolean(
                        d
                        &&
                        toEnd
                        &&
                        d <= toEnd
                      )
                    : Boolean(
                        d
                        &&
                        fromStart
                        &&
                        toEnd
                        &&
                        d >= fromStart
                        &&
                        d <= toEnd
                      );

            return (
              matchesSearch
              &&
              matchesStatus
              &&
              matchesDate
            );
          }
        ),
      [
        summaries,
        search,
        status,
        from,
        to
      ]
    );


  useEffect(
    () =>
      setPage(1),
    [
      search,
      status,
      from,
      to
    ]
  );


  // ============================================================
  // PAGINATION
  // ============================================================

  const pages =
    Math.max(
      1,
      Math.ceil(
        filtered.length
        /
        10
      )
    );


  const visible =
    filtered.slice(
      (page - 1) * 10,
      page * 10
    );


  // ============================================================
  // GRN DETAIL
  // ============================================================

  const allGrns =
    useMemo(
      () =>
        selected
          ?
          toGrns(
            selected.rows
          )
          :
          [],
      [
        selected
      ]
    );


  const shownGrns =
    grnView === "available"
      ?
      allGrns.filter(
        grn =>
          grn.rows
            .some(
              hasAvailable
            )
      )
      :
      allGrns;


  const invoiceStatusForGrn = (
    grnNumber: string
  ) =>
  {
    const matchingInvoices =
      invoices
        .filter(
          invoice =>
            invoiceForGrn(
              invoice,
              grnNumber
            )
        )
        .sort(
          (a, b) =>
          {
            const aDate =
              new Date(
                a.updatedAt
                || a.submissionDate
                || 0
              ).getTime();

            const bDate =
              new Date(
                b.updatedAt
                || b.submissionDate
                || 0
              ).getTime();

            return bDate - aDate;
          }
        );

    if (!matchingInvoices.length)
    {
      return "Not Invoiced";
    }

    return presentInvoiceStatus(
      matchingInvoices[0]
    );
  };


  const totals =
    shownGrns.reduce(
      (
        sum,
        grn
      ) =>
        ({
          received:
            sum.received
            +
            grn.received,

          available:
            sum.available
            +
            grn.available,

          amount:
            sum.amount
            +
            grn.amount,

          availableAmount:
            sum.availableAmount
            +
            grn.availableAmount
        }),
      {
        received: 0,
        available: 0,
        amount: 0,
        availableAmount: 0
      }
    );


  // ============================================================
  // EXPORT
  // ============================================================

  const exportRows =
    () =>
      exportRowsToCsv(
        "purchase-orders.csv",

        [
          "PO Number",
          "PO Date",
          "PO Amount",
          "Available to Invoice",
          "QC Status",
          "Available GRNs"
        ],

        filtered.map(
          row =>
            [
              row.poNumber,
              date(
                row.poDate
              ),
              row.poAmount,
              row.availableAmount,
              row.qcStatus,
              row.availableGrns
            ]
        )
      );


  // ============================================================
  // SUBMIT INVOICE
  // ============================================================

  const submitInvoice =
    () =>
    {
      if (
        !selected
        ||
        !selected.availableLines
      )
      {
        return;
      }

      const query =
        new URLSearchParams(
          {
            po:
              selected.poNumber,

            prefill:
              "1"
          }
        );

      allGrns
        .filter(
          grn =>
            grn.rows
              .some(
                hasAvailable
              )
        )
        .forEach(
          grn =>
            query.append(
              "grn",
              grn.number
            )
        );

      navigate(
        `/invoices/new?${query}`
      );
    };


  // ============================================================
  // UI
  // ============================================================

  return (
    <>
      <div className="page-card po-experience">

        <div className="page-card-head vendor-table-head">

          <div>
            <h2>
              Purchase Orders
            </h2>

            <p>
              Open purchase orders and live GRN availability from Oracle EBS.
            </p>
          </div>

          <DataTableToolbar
            searchValue={search}
            onSearchChange={setSearch}
            searchPlaceholder="Search Purchase Orders..."
            statusValue={status}
            onStatusChange={setStatus}
            statusOptions={statusOptions}
            statusLabel="QC Status"
            showDateFilter
            fromDate={from}
            toDate={to}
            onApplyDateRange={
              (
                start,
                end
              ) =>
              {
                setFrom(start);
                setTo(end);
              }
            }
            onExport={exportRows}
          />

        </div>


        <table className="data-table vendor-data-table po-summary-table">

          <thead>
            <tr>
              <th>
                PO Number
              </th>

              <th>
                PO Date
              </th>

              <th>
                PO Amount
              </th>

              <th>
                Available to Invoice
              </th>

              <th>
                QC Status
              </th>

              <th>
                Available GRNs
              </th>

              <th>
                Action
              </th>
            </tr>
          </thead>


          <tbody>

            {visible.map(
              row =>
                (
                  <tr
                    key={
                      row.poNumber
                    }
                    onClick={
                      () =>
                      {
                        setSelected(
                          row
                        );

                        setGrnView(
                          "available"
                        );

                        setExpanded(
                          new Set()
                        );
                      }
                    }
                  >

                    <td>
                      <strong>
                        {row.poNumber}
                      </strong>
                    </td>

                    <td>
                      {date(
                        row.poDate
                      )}
                    </td>

                    <td>
                      {money(
                        row.poAmount
                      )}
                    </td>

                    <td>
                      {money(
                        row.availableAmount
                      )}
                    </td>

                    <td>
                      <span
                        className={
                          `status ${qcClass(
                            row.qcStatus
                          )}`
                        }
                      >
                        {row.qcStatus}
                      </span>
                    </td>

                    <td>
                      {row.availableGrns}
                    </td>

                    <td>

                      <button
                        className="table-btn"
                        onClick={
                          event =>
                          {
                            event
                              .stopPropagation();

                            setSelected(
                              row
                            );

                            setGrnView(
                              "available"
                            );

                            setExpanded(
                              new Set()
                            );
                          }
                        }
                      >
                        View Details
                      </button>

                    </td>

                  </tr>
                )
            )}


            {!visible.length && (
              <tr>
                <td
                  className="empty"
                  colSpan={7}
                >
                  No purchase orders match the selected filters.
                </td>
              </tr>
            )}

          </tbody>

        </table>


        {filtered.length > 10 && (
          <div className="po-pagination">

            <span>
              {filtered.length} purchase orders
            </span>

            <div>

              <button
                disabled={
                  page === 1
                }
                onClick={
                  () =>
                    setPage(
                      value =>
                        value - 1
                    )
                }
              >
                Previous
              </button>

              <span>
                Page {page} of {pages}
              </span>

              <button
                disabled={
                  page === pages
                }
                onClick={
                  () =>
                    setPage(
                      value =>
                        value + 1
                    )
                }
              >
                Next
              </button>

            </div>

          </div>
        )}

      </div>


      {selected && (

        <aside
          className="po-drawer"
          role="dialog"
          aria-modal="true"
        >

          <div
            className="po-drawer-backdrop"
            onClick={
              () =>
                setSelected(null)
            }
          />


          <section className="po-drawer-panel">

            <header>

              <div>

                <small>
                  Purchase Order
                </small>

                <h2>
                  {selected.poNumber}
                </h2>

                <span className="status green">
                  {selected.status}
                </span>

              </div>


              <button
                className="po-drawer-close"
                onClick={
                  () =>
                    setSelected(null)
                }
                aria-label="Close"
              >
                ×
              </button>

            </header>


            <div className="po-detail-meta">

              <div>
                <small>
                  Vendor Name
                </small>

                <b>
                  {
                    selected.rows[0]
                      ?.vendorName
                    || "-"
                  }
                </b>
              </div>


              <div>
                <small>
                  PO Date
                </small>

                <b>
                  {date(
                    selected.poDate
                  )}
                </b>
              </div>


              <div>
                <small>
                  Currency
                </small>

                <b>
                  {selected.currency}
                </b>
              </div>


              <div>
                <small>
                  Oracle Vendor ID
                </small>

                <b>
                  {
                    selected.rows[0]
                      ?.vendorId
                    || "-"
                  }
                </b>
              </div>


              {
                selected.rows[0]
                  ?.poType
                &&
                (
                  <div>
                    <small>
                      PO Type
                    </small>

                    <b>
                      {
                        selected.rows[0]
                          .poType
                      }
                    </b>
                  </div>
                )
              }

            </div>


            <div className="po-summary-cards">

              {[
                [
                  "PO Amount",
                  money(
                    selected.poAmount
                  )
                ],

                [
                  "Invoiced Amount",
                  money(
                    selected.invoiced
                  )
                ],

                [
                  "Open Amount",
                  money(
                    selected.open
                  )
                ],

                [
                  "Available to Invoice",
                  money(
                    selected.availableAmount
                  )
                ],

                [
                  "Related GRNs",
                  String(
                    selected.totalGrns
                  )
                ],

                [
                  "Available GRNs",
                  String(
                    selected.availableGrns
                  )
                ],

                [
                  "Available GRN Lines",
                  String(
                    selected.availableLines
                  )
                ]

              ].map(
                (
                  [
                    label,
                    value
                  ]
                ) =>
                  (
                    <div
                      key={
                        label
                      }
                    >

                      <small>
                        {label}
                      </small>

                      <b>
                        {value}
                      </b>

                    </div>
                  )
              )}

            </div>


            <div className="po-section-head">

              <div>

                <h3>
                  Related GRNs
                </h3>

                <p>
                  Receipt lines are deduplicated by Oracle GRN shipment line.
                </p>

              </div>

            </div>


            <div className="po-grn-tabs">

              <button
                className={
                  grnView ===
                  "available"
                    ?
                    "active"
                    :
                    ""
                }
                onClick={
                  () =>
                    setGrnView(
                      "available"
                    )
                }
              >
                Available GRNs ({selected.availableGrns})
              </button>


              <button
                className={
                  grnView ===
                  "all"
                    ?
                    "active"
                    :
                    ""
                }
                onClick={
                  () =>
                    setGrnView(
                      "all"
                    )
                }
              >
                All GRNs ({selected.totalGrns})
              </button>

            </div>


            <div className="po-grn-totals">

              <span>
                Total GRNs{" "}
                <b>
                  {shownGrns.length}
                </b>
              </span>


              <span>
                Received Qty{" "}
                <b>
                  {qty(
                    totals.received
                  )}
                </b>
              </span>


              <span>
                Available Qty{" "}
                <b>
                  {qty(
                    totals.available
                  )}
                </b>
              </span>


              <span>
                GRN Amount{" "}
                <b>
                  {money(
                    totals.amount
                  )}
                </b>
              </span>


              <span>
                Available GRN Amount{" "}
                <b>
                  {money(
                    totals.availableAmount
                  )}
                </b>
              </span>

            </div>


            <div className="po-table-scroll">

              <table className="data-table po-grn-table">

                <thead>

                  <tr>

                    <th>
                      GRN Number
                    </th>

                    <th>
                      Receipt Date
                    </th>

                    <th>
                      Lines
                    </th>

                    <th>
                      Received Qty
                    </th>

                    <th>
                      Available Qty
                    </th>

                    <th>
                      Amount ({selected.currency})
                    </th>

                    <th>
                      QC Status
                    </th>

                    <th style={{ paddingRight: "28px" }}>
                      Invoice Status
                    </th>

                    <th style={{ paddingLeft: "12px" }}>
                      Action
                    </th>

                  </tr>

                </thead>


                <tbody>

                  {shownGrns.map(
                    grn =>
                      (
                        <Fragment
                          key={
                            grn.number
                          }
                        >

                          <tr>

                            <td>
                              <strong>
                                {grn.number}
                              </strong>
                            </td>

                            <td>
                              {date(
                                grn.rows[0]
                                  ?.receiptDate
                              )}
                            </td>

                            <td>
                              {grn.rows.length}
                            </td>

                            <td>
                              {qty(
                                grn.received
                              )}
                            </td>

                            <td>
                              {qty(
                                grn.available
                              )}
                            </td>

                            <td>
                              {money(
                                grn.amount
                              )}
                            </td>

                            <td>
                              <span
                                className={
                                  `status ${qcClass(
                                    grn.status
                                  )}`
                                }
                              >
                                {grn.status}
                              </span>
                            </td>

                            <td style={{ paddingRight: "28px" }}>
                              {(() =>
                              {
                                const invoiceStatus =
                                  invoiceStatusForGrn(
                                    grn.number
                                  );

                                return (
                                  <span
                                    className={
                                      `status ${invoiceStatusClass(
                                        invoiceStatus
                                      )}`
                                    }
                                  >
                                    {invoiceStatus}
                                  </span>
                                );
                              })()}
                            </td>

                            <td style={{ paddingLeft: "12px" }}>

                              <button
                                className="table-btn"
                                onClick={
                                  () =>
                                    setExpanded(
                                      current =>
                                      {
                                        const next =
                                          new Set(
                                            current
                                          );

                                        if (
                                          next.has(
                                            grn.number
                                          )
                                        )
                                        {
                                          next.delete(
                                            grn.number
                                          );
                                        }
                                        else
                                        {
                                          next.add(
                                            grn.number
                                          );
                                        }

                                        return next;
                                      }
                                    )
                                }
                              >
                                {
                                  expanded.has(
                                    grn.number
                                  )
                                    ?
                                    "Hide Lines"
                                    :
                                    "View Lines"
                                }
                              </button>

                            </td>

                          </tr>


                          {
                            expanded.has(
                              grn.number
                            )
                            &&
                            (
                              <tr>

                                <td
                                  className="po-grn-lines"
                                  colSpan={9}
                                >

                                  <table>

                                    <thead>

                                      <tr>

                                        <th>
                                          Line No
                                        </th>

                                        {
                                          grn.rows.some(
                                            row =>
                                              row.itemCode
                                          )
                                          &&
                                          (
                                            <th>
                                              Item Code
                                            </th>
                                          )
                                        }

                                        {
                                          grn.rows.some(
                                            row =>
                                              row.itemDescription
                                          )
                                          &&
                                          (
                                            <th>
                                              Item Description
                                            </th>
                                          )
                                        }

                                        <th>
                                          Received Qty
                                        </th>

                                        <th>
                                          Available Qty
                                        </th>

                                        <th>
                                          Unit Price
                                        </th>

                                        <th>
                                          Amount
                                        </th>

                                      </tr>

                                    </thead>


                                    <tbody>

                                      {grn.rows.map(
                                        row =>
                                          (
                                            <tr
                                              key={
                                                receiptKey(
                                                  row
                                                )
                                              }
                                            >

                                              <td>
                                                {
                                                  row.poLineNum
                                                  || "-"
                                                }
                                              </td>


                                              {
                                                grn.rows.some(
                                                  item =>
                                                    item.itemCode
                                                )
                                                &&
                                                (
                                                  <td>
                                                    {
                                                      row.itemCode
                                                      || "-"
                                                    }
                                                  </td>
                                                )
                                              }


                                              {
                                                grn.rows.some(
                                                  item =>
                                                    item.itemDescription
                                                )
                                                &&
                                                (
                                                  <td>
                                                    {
                                                      row.itemDescription
                                                      || "-"
                                                    }
                                                  </td>
                                                )
                                              }


                                              <td>
                                                {qty(
                                                  Number(
                                                    row.grnReceivedQuantity
                                                    ??
                                                    row.receivedQuantity
                                                    ??
                                                    0
                                                  )
                                                )}
                                              </td>


                                              <td>
                                                {qty(
                                                  Number(
                                                    row.quantityAvailableToInvoice
                                                    ||
                                                    0
                                                  )
                                                )}
                                              </td>


                                              <td>
                                                {money(
                                                  Number(
                                                    row.unitPrice
                                                    ||
                                                    0
                                                  )
                                                )}
                                              </td>


                                              <td>
                                                {money(
                                                  Number(
                                                    row.grnReceivedQuantity
                                                    ??
                                                    row.receivedQuantity
                                                    ??
                                                    0
                                                  )
                                                  *
                                                  Number(
                                                    row.unitPrice
                                                    ||
                                                    0
                                                  )
                                                )}
                                              </td>

                                            </tr>
                                          )
                                      )}

                                    </tbody>

                                  </table>

                                </td>

                              </tr>
                            )
                          }

                        </Fragment>
                      )
                  )}


                  {!shownGrns.length && (

                    <tr>

                      <td
                        className="empty"
                        colSpan={9}
                      >
                        {
                          grnView ===
                          "available"
                            ?
                            "No eligible GRN available for invoicing."
                            :
                            "No related GRNs found."
                        }
                      </td>

                    </tr>

                  )}

                </tbody>

              </table>

            </div>


            <footer>

              <div>

                {!selected.availableLines && (

                  <span className="po-submit-note">
                    No eligible GRN available for invoicing.
                  </span>

                )}

              </div>


              <button
                className="secondary-btn"
                onClick={
                  () =>
                    setSelected(null)
                }
              >
                Close
              </button>


              <button
                className="primary-btn"
                disabled={
                  !selected.availableLines
                }
                onClick={
                  submitInvoice
                }
              >
                Submit Invoice
              </button>

            </footer>

          </section>

        </aside>

      )}

    </>
  );
}