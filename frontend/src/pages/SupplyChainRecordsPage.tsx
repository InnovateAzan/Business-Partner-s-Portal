import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  Navigate,
} from "react-router-dom";

import {
  useAuth,
} from "../auth/AuthContext";

import {
  getLiveDashboard,
} from "../api/portal";

import {
  exportRowsToCsv,
} from "../utils/exportCsv";

type PageKind =
  | "po"
  | "grn"
  | "pending"
  | "onboarded";

type Props = {
  kind: PageKind;
};

type DashboardData = any;

const PAGE_CONFIG: Record<
  PageKind,
  {
    title: string;
    description: string;
    permission: string;
    exportName: string;
  }
> = {
  po: {
    title: "Recent Purchase Orders",
    description:
      "Recent purchase orders visible to Supply Chain from live portal data.",
    permission:
      "PO.VIEW",
    exportName:
      "supply-chain-purchase-orders.csv",
  },
  grn: {
    title: "Recent GRNs",
    description:
      "Recent GRNs and QC status visible to Supply Chain from live portal data.",
    permission:
      "GRN.VIEW",
    exportName:
      "supply-chain-grns.csv",
  },
  pending: {
    title: "Pending Vendor Requests",
    description:
      "Vendor access requests that still require Supply Chain action.",
    permission:
      "VENDOR.MANAGE",
    exportName:
      "pending-vendor-access.csv",
  },
  onboarded: {
    title: "Recently Onboarded Vendors",
    description:
      "Recently enabled vendor portal accounts and onboarding activity.",
    permission:
      "VENDOR.VIEW",
    exportName:
      "recently-onboarded-vendors.csv",
  },
};

function formatDate(
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
    return String(value);
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

function formatMoney(
  value?: number | null
) {
  return Number(
    value || 0
  ).toLocaleString();
}

function inRange(
  value: string | null | undefined,
  from: string,
  to: string
) {
  if (
    !from &&
    !to
  ) {
    return true;
  }

  if (!value) {
    return false;
  }

  const date =
    new Date(value);

  if (
    Number.isNaN(
      date.getTime()
    )
  ) {
    return false;
  }

  if (
    from &&
    date <
      new Date(
        `${from}T00:00:00`
      )
  ) {
    return false;
  }

  if (
    to &&
    date >
      new Date(
        `${to}T23:59:59`
      )
  ) {
    return false;
  }

  return true;
}

function StatusPill({
  value,
}: {
  value: string;
}) {
  const normalized =
    (value || "-")
      .toLowerCase();

  const className =
    normalized.includes("reject") ||
    normalized.includes("fail") ||
    normalized.includes("disabled")
      ? "red"
      : normalized.includes("pending")
        ? "orange"
        : normalized.includes("review") ||
            normalized.includes("partial") ||
            normalized.includes("not invoiced")
          ? "blue"
          : "green";

  return (
    <span
      className={`role-status ${className}`}
    >
      {value || "-"}
    </span>
  );
}

function openDatePicker(
  input: HTMLInputElement
) {
  if (
    typeof input.showPicker ===
    "function"
  ) {
    input.showPicker();
  }
}

export function SupplyChainRecordsPage({
  kind,
}: Props) {
  const {
    hasPermission,
  } = useAuth();

  const config =
    PAGE_CONFIG[kind];

  const [
    data,
    setData,
  ] = useState<DashboardData | null>(
    null
  );

  const [
    error,
    setError,
  ] = useState("");

  const [
    from,
    setFrom,
  ] = useState("");

  const [
    to,
    setTo,
  ] = useState("");

  const [
    vendor,
    setVendor,
  ] = useState("");

  const [
    status,
    setStatus,
  ] = useState("");

  const [
    search,
    setSearch,
  ] = useState("");

  const [
    applied,
    setApplied,
  ] = useState({
    from: "",
    to: "",
    vendor: "",
    status: "",
    search: "",
  });

  const [
    page,
    setPage,
  ] = useState(1);

  const pageSize = 15;

  useEffect(
    () => {
      if (
        !hasPermission(
          config.permission
        )
      ) {
        return;
      }

      getLiveDashboard()
        .then(
          setData
        )
        .catch(
          (err: any) =>
            setError(
              err?.response?.data?.message ||
                err?.message ||
                "Unable to load Supply Chain data."
            )
        );
    },
    [
      config.permission,
      hasPermission,
    ]
  );

  const source: any[] =
    useMemo(
      () => {
        const supply =
          data?.supplyChain || {};

        return kind === "po"
          ? supply.purchaseOrders || []
          : kind === "grn"
            ? supply.grns || []
            : kind === "pending"
              ? supply.pendingVendorRequests || []
              : supply.recentlyOnboardedVendors || [];
      },
      [
        data,
        kind,
      ]
    );

  const vendors =
    useMemo(
      () =>
        Array.from(
          new Set(
            source
              .map(
                (row: any) =>
                  row.vendorName
              )
              .filter(Boolean)
          )
        ).sort() as string[],
      [source]
    );

  const statuses =
    useMemo(
      () =>
        Array.from(
          new Set(
            source
              .map(
                (row: any) =>
                  row.status ||
                  row.accessStatus ||
                  (
                    row.active
                      ? "Active"
                      : "Inactive"
                  )
              )
              .filter(Boolean)
          )
        ).sort() as string[],
      [source]
    );

  const filtered =
    useMemo(
      () =>
        source.filter(
          (row: any) => {
            const date =
              row.poDate ||
              row.grnDate ||
              row.onboardedAt ||
              row.lastAccess;

            const rowStatus =
              row.status ||
              row.accessStatus ||
              (
                row.active
                  ? "Active"
                  : "Inactive"
              );

            const query =
              applied.search
                .trim()
                .toLowerCase();

            const searchable =
              Object.values(row)
                .join(" ")
                .toLowerCase();

            return (
              inRange(
                date,
                applied.from,
                applied.to
              ) &&
              (
                !applied.vendor ||
                row.vendorName ===
                  applied.vendor
              ) &&
              (
                !applied.status ||
                rowStatus ===
                  applied.status
              ) &&
              (
                !query ||
                searchable.includes(
                  query
                )
              )
            );
          }
        ),
      [
        source,
        applied,
      ]
    );

  useEffect(
    () => {
      setPage(1);
    },
    [
      applied,
      kind,
    ]
  );

  if (
    !hasPermission(
      config.permission
    )
  ) {
    return (
      <Navigate
        to="/internal"
        replace
      />
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

  if (!data) {
    return (
      <div className="page-card">
        <p>
          Loading live Supply Chain data...
        </p>
      </div>
    );
  }

  const pages =
    Math.max(
      1,
      Math.ceil(
        filtered.length /
          pageSize
      )
    );

  const safePage =
    Math.min(
      page,
      pages
    );

  const visible =
    filtered.slice(
      (
        safePage - 1
      ) * pageSize,
      safePage * pageSize
    );

  function clearFilters() {
    setFrom("");
    setTo("");
    setVendor("");
    setStatus("");
    setSearch("");
    setApplied({
      from: "",
      to: "",
      vendor: "",
      status: "",
      search: "",
    });
  }

  function exportRows() {
    if (
      kind === "po"
    ) {
      exportRowsToCsv(
        config.exportName,
        [
          "PO Number",
          "Vendor",
          "PO Date",
          "Amount (PKR)",
          "Status",
          "GRN Status",
          "Invoice Status",
        ],
        filtered.map(
          (row: any) => [
            row.poNumber,
            row.vendorName,
            formatDate(
              row.poDate
            ),
            row.amount,
            row.status,
            row.grnStatus,
            row.invoiceStatus,
          ]
        )
      );

      return;
    }

    if (
      kind === "grn"
    ) {
      exportRowsToCsv(
        config.exportName,
        [
          "GRN Number",
          "Vendor",
          "PO Number",
          "GRN Date",
          "Status",
          "QC Status",
        ],
        filtered.map(
          (row: any) => [
            row.grnNumber,
            row.vendorName,
            row.poNumber,
            formatDate(
              row.grnDate
            ),
            row.status,
            row.qcStatus,
          ]
        )
      );

      return;
    }

    exportRowsToCsv(
      config.exportName,
      [
        "Vendor",
        "Oracle Vendor ID",
        "Status",
        "Portal Users",
        "Last Access / Onboarded",
      ],
      filtered.map(
        (row: any) => [
          row.vendorName,
          row.oracleVendorId || "",
          row.accessStatus ||
            (
              row.active
                ? "Active"
                : "Inactive"
            ),
          row.portalUsers ??
            row.activePortalUsers ??
            1,
          formatDate(
            row.lastAccess ||
              row.onboardedAt
          ),
        ]
      )
    );
  }

  return (
    <div className="page-card supply-records-page">
      <div className="page-card-head supply-records-head">
        <div>
          <h2>
            {config.title}
          </h2>

          <p>
            {config.description}
          </p>
        </div>

        <button
          type="button"
          className="primary-btn"
          onClick={
            exportRows
          }
        >
          Export to Excel
        </button>
      </div>

      <div className="role-filters supply-records-filters">
        <label>
          <span>
            From Date
          </span>

          <input
            type="date"
            value={from}
            onClick={
              event =>
                openDatePicker(
                  event.currentTarget
                )
            }
            onChange={
              event =>
                setFrom(
                  event.target.value
                )
            }
          />
        </label>

        <label>
          <span>
            To Date
          </span>

          <input
            type="date"
            value={to}
            onClick={
              event =>
                openDatePicker(
                  event.currentTarget
                )
            }
            onChange={
              event =>
                setTo(
                  event.target.value
                )
            }
          />
        </label>

        <label>
          <span>
            Vendor
          </span>

          <select
            value={vendor}
            onChange={
              event =>
                setVendor(
                  event.target.value
                )
            }
          >
            <option value="">
              All Vendors
            </option>

            {vendors.map(
              value => (
                <option
                  key={value}
                  value={value}
                >
                  {value}
                </option>
              )
            )}
          </select>
        </label>

        <label>
          <span>
            Status
          </span>

          <select
            value={status}
            onChange={
              event =>
                setStatus(
                  event.target.value
                )
            }
          >
            <option value="">
              All Statuses
            </option>

            {statuses.map(
              value => (
                <option
                  key={value}
                  value={value}
                >
                  {value}
                </option>
              )
            )}
          </select>
        </label>

        <label className="role-search-box">
          <span>
            Search
          </span>

          <input
            value={search}
            onChange={
              event =>
                setSearch(
                  event.target.value
                )
            }
            placeholder="Search records..."
          />
        </label>

        <button
          type="button"
          className="role-search"
          onClick={() =>
            setApplied({
              from,
              to,
              vendor,
              status,
              search,
            })
          }
        >
          Search
        </button>

        <button
          type="button"
          className="role-clear"
          onClick={
            clearFilters
          }
        >
          Clear
        </button>
      </div>

      <div className="role-table-scroll supply-records-table-wrap">
        {kind === "po" ? (
          <table className="role-table">
            <thead>
              <tr>
                <th>PO Number</th>
                <th>Vendor</th>
                <th>PO Date</th>
                <th>Amount (PKR)</th>
                <th>Status</th>
                <th>GRN Status</th>
                <th>Invoice Status</th>
              </tr>
            </thead>

            <tbody>
              {visible.map(
                (row: any) => (
                  <tr
                    key={
                      row.id ||
                      `${row.poNumber}-${row.vendorName}`
                    }
                  >
                    <td>
                      {row.poNumber}
                    </td>
                    <td>
                      {row.vendorName}
                    </td>
                    <td>
                      {formatDate(
                        row.poDate
                      )}
                    </td>
                    <td>
                      {formatMoney(
                        row.amount
                      )}
                    </td>
                    <td>
                      <StatusPill
                        value={
                          row.status
                        }
                      />
                    </td>
                    <td>
                      <StatusPill
                        value={
                          row.grnStatus
                        }
                      />
                    </td>
                    <td>
                      <StatusPill
                        value={
                          row.invoiceStatus
                        }
                      />
                    </td>
                  </tr>
                )
              )}

              {!visible.length && (
                <tr>
                  <td
                    colSpan={7}
                    className="empty"
                  >
                    No records match the selected filters.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        ) : kind === "grn" ? (
          <table className="role-table">
            <thead>
              <tr>
                <th>GRN Number</th>
                <th>Vendor</th>
                <th>PO Number</th>
                <th>GRN Date</th>
                <th>Status</th>
                <th>QC Status</th>
              </tr>
            </thead>

            <tbody>
              {visible.map(
                (row: any) => (
                  <tr
                    key={
                      row.id ||
                      `${row.grnNumber}-${row.poNumber}`
                    }
                  >
                    <td>
                      {row.grnNumber}
                    </td>
                    <td>
                      {row.vendorName}
                    </td>
                    <td>
                      {row.poNumber}
                    </td>
                    <td>
                      {formatDate(
                        row.grnDate
                      )}
                    </td>
                    <td>
                      <StatusPill
                        value={
                          row.status
                        }
                      />
                    </td>
                    <td>
                      {row.qcStatus ||
                        "-"}
                    </td>
                  </tr>
                )
              )}

              {!visible.length && (
                <tr>
                  <td
                    colSpan={6}
                    className="empty"
                  >
                    No records match the selected filters.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        ) : (
          <table className="role-table">
            <thead>
              <tr>
                <th>Vendor</th>
                <th>Oracle Vendor ID</th>
                <th>Status</th>
                <th>Portal Users</th>
                <th>Last Access / Onboarded</th>
              </tr>
            </thead>

            <tbody>
              {visible.map(
                (
                  row: any,
                  index: number
                ) => (
                  <tr
                    key={
                      row.vendorId ||
                      `${row.vendorName}-${index}`
                    }
                  >
                    <td>
                      {row.vendorName}
                    </td>
                    <td>
                      {row.oracleVendorId ||
                        "-"}
                    </td>
                    <td>
                      <StatusPill
                        value={
                          row.accessStatus ||
                          (
                            row.active
                              ? "Active"
                              : "Inactive"
                          )
                        }
                      />
                    </td>
                    <td>
                      {row.portalUsers ??
                        row.activePortalUsers ??
                        1}
                    </td>
                    <td>
                      {formatDate(
                        row.lastAccess ||
                          row.onboardedAt
                      )}
                    </td>
                  </tr>
                )
              )}

              {!visible.length && (
                <tr>
                  <td
                    colSpan={5}
                    className="empty"
                  >
                    No records match the selected filters.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>

      <div className="role-pagination">
        <span>
          Showing{" "}
          {filtered.length
            ? (
                safePage - 1
              ) * pageSize +
              1
            : 0}
          {" "}to{" "}
          {Math.min(
            filtered.length,
            safePage *
              pageSize
          )}{" "}
          of{" "}
          {filtered.length.toLocaleString()}{" "}
          records
        </span>

        <div>
          <button
            type="button"
            disabled={
              safePage <= 1
            }
            onClick={() =>
              setPage(
                safePage - 1
              )
            }
          >
            ‹
          </button>

          <span className="supply-page-number">
            Page {safePage} of {pages}
          </span>

          <button
            type="button"
            disabled={
              safePage >= pages
            }
            onClick={() =>
              setPage(
                safePage + 1
              )
            }
          >
            ›
          </button>
        </div>
      </div>
    </div>
  );
}
