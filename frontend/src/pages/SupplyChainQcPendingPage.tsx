import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  getQcPendingGrns,
} from "../api/supplyChain";

import type {
  QcPendingGrn,
} from "../api/supplyChain";

import {
  exportRowsToCsv,
} from "../utils/exportCsv";

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
    return value;
  }

  return date.toLocaleDateString(
    "en-GB",
    {
      day:
        "2-digit",

      month:
        "short",

      year:
        "numeric",
    }
  );
}

function formatQuantity(
  value?: number | null
) {
  if (
    value === null
    ||
    value === undefined
  ) {
    return "-";
  }

  return Number(
    value
  ).toLocaleString(
    "en-US",
    {
      maximumFractionDigits:
        3,
    }
  );
}

function displayValue(
  value?: string | null
) {
  if (
    !value
    ||
    !value.trim()
  ) {
    return "-";
  }

  return value;
}

export function SupplyChainQcPendingPage() {
  const [
    rows,
    setRows,
  ] =
    useState<
      QcPendingGrn[]
    >([]);

  const [
    loading,
    setLoading,
  ] =
    useState(
      true
    );

  const [
    error,
    setError,
  ] =
    useState(
      ""
    );

  const [
    search,
    setSearch,
  ] =
    useState(
      ""
    );

  const [
    vendor,
    setVendor,
  ] =
    useState(
      ""
    );

  const [
    page,
    setPage,
  ] =
    useState(
      1
    );

  const [
    selectedRow,
    setSelectedRow,
  ] =
    useState<
      QcPendingGrn | null
    >(
      null
    );

  const pageSize =
    15;

  // ============================================================
  // LOAD DATA
  // ============================================================

  useEffect(() => {
    let active =
      true;

    setLoading(
      true
    );

    setError(
      ""
    );

    getQcPendingGrns()
      .then(
        (
          data
        ) => {
          if (
            active
          ) {
            setRows(
              Array.isArray(
                data
              )
                ? data
                : []
            );
          }
        }
      )
      .catch(
        (
          err: any
        ) => {
          if (
            active
          ) {
            setError(
              err
                ?.response
                ?.data
                ?.message
              ||
              err
                ?.message
              ||
              "Unable to load QC pending GRNs."
            );
          }
        }
      )
      .finally(
        () => {
          if (
            active
          ) {
            setLoading(
              false
            );
          }
        }
      );

    return () => {
      active =
        false;
    };
  }, []);

  // ============================================================
  // CLOSE DETAILS WITH ESCAPE
  // ============================================================

  useEffect(() => {
    const handleKeyDown =
      (
        event:
          KeyboardEvent
      ) => {
        if (
          event.key ===
          "Escape"
        ) {
          setSelectedRow(
            null
          );
        }
      };

    document.addEventListener(
      "keydown",
      handleKeyDown
    );

    return () => {
      document.removeEventListener(
        "keydown",
        handleKeyDown
      );
    };
  }, []);

  // ============================================================
  // VENDORS
  // ============================================================

  const vendors =
    useMemo(
      () =>
        Array.from(
          new Set(
            rows
              .map(
                (
                  row
                ) =>
                  row.vendorName
              )
              .filter(
                Boolean
              )
          )
        )
          .sort(
            (
              a,
              b
            ) =>
              a.localeCompare(
                b
              )
          ),
      [
        rows,
      ]
    );

  // ============================================================
  // FILTER
  // ============================================================

  const filtered =
    useMemo(
      () => {
        const query =
          search
            .trim()
            .toLowerCase();

        return rows.filter(
          (
            row
          ) => {
            if (
              vendor
              &&
              row.vendorName !==
                vendor
            ) {
              return false;
            }

            if (
              !query
            ) {
              return true;
            }

            return [
              row.vendorName,
              row.vendorCode,
              row.oracleVendorId,
              row.poNumber,
              row.prNumber,
              row.grnNumber,
              row.poLineNumber,
              row.itemCode,
              row.itemDescription,
              row.qcStatus,
              row.oracleQcStatus,
            ]
              .filter(
                Boolean
              )
              .join(
                " "
              )
              .toLowerCase()
              .includes(
                query
              );
          }
        );
      },
      [
        rows,
        search,
        vendor,
      ]
    );

  useEffect(() => {
    setPage(
      1
    );
  }, [
    search,
    vendor,
  ]);

  // ============================================================
  // PAGINATION
  // ============================================================

  const totalPages =
    Math.max(
      1,
      Math.ceil(
        filtered.length
        /
        pageSize
      )
    );

  const safePage =
    Math.min(
      page,
      totalPages
    );

  const visibleRows =
    filtered.slice(
      (
        safePage -
        1
      )
      *
      pageSize,

      safePage
      *
      pageSize
    );

  // ============================================================
  // EXPORT
  // ============================================================

  function exportRows() {
    exportRowsToCsv(
      "qc-pending-grns.csv",

      [
        "Vendor",
        "Vendor Code",
        "Oracle Vendor ID",
        "PO Number",
        "PO Date",
        "PR Number",
        "GRN Number",
        "GRN Date",
        "PO Line",
        "Item Code",
        "Item Description",
        "Received Quantity",
        "UOM",
        "QC Status",
        "Oracle QC Status",
        "Aging Days",
      ],

      filtered.map(
        (
          row
        ) => [
          row.vendorName,

          row.vendorCode,

          row.oracleVendorId,

          row.poNumber,

          formatDate(
            row.poDate
          ),

          row.prNumber
          ||
          "-",

          row.grnNumber
          ||
          "-",

          formatDate(
            row.grnDate
          ),

          row.poLineNumber
          ||
          "-",

          row.itemCode
          ||
          "-",

          row.itemDescription
          ||
          "-",

          row.receivedQuantity
          ??
          "-",

          row.uom
          ||
          "-",

          row.qcStatus,

          row.oracleQcStatus
          ||
          "-",

          row.agingDays,
        ]
      )
    );
  }

  // ============================================================
  // DETAILS
  // ============================================================

  function openDetails(
    row:
      QcPendingGrn
  ) {
    setSelectedRow(
      row
    );
  }

  function closeDetails() {
    setSelectedRow(
      null
    );
  }

  // ============================================================
  // PAGE
  // ============================================================

  return (
    <section className="page-card">

      {/* ========================================================
          HEADER
      ======================================================== */}

      <div
        className="page-card-head"
        style={{
          alignItems:
            "center",

          gap:
            "16px",

          flexWrap:
            "wrap",
        }}
      >
        <div>
          <h2>
            QC Pending GRNs
          </h2>

          <p>
            GRNs pending Quality Control for active, onboarded portal vendors only.
          </p>
        </div>

        <div
          style={{
            marginLeft:
              "auto",

            display:
              "flex",

            alignItems:
              "center",

            justifyContent:
              "flex-end",

            gap:
              "10px",

            flexWrap:
              "wrap",
          }}
        >
          <input
            value={
              search
            }
            onChange={
              (
                event
              ) =>
                setSearch(
                  event
                    .target
                    .value
                )
            }
            placeholder="Search vendor, PO, PR or item..."
            style={{
              width:
                "320px",

              minWidth:
                "240px",

              height:
                "42px",

              border:
                "1px solid #d7dfdc",

              borderRadius:
                "7px",

              padding:
                "0 12px",

              background:
                "#ffffff",

              outline:
                "none",

              boxSizing:
                "border-box",
            }}
          />

          <select
            value={
              vendor
            }
            onChange={
              (
                event
              ) =>
                setVendor(
                  event
                    .target
                    .value
                )
            }
            style={{
              width:
                "235px",

              minWidth:
                "190px",

              height:
                "42px",

              border:
                "1px solid #d7dfdc",

              borderRadius:
                "7px",

              padding:
                "0 12px",

              background:
                "#ffffff",

              outline:
                "none",

              boxSizing:
                "border-box",
            }}
          >
            <option value="">
              All Active Vendors
            </option>

            {vendors.map(
              (
                name
              ) => (
                <option
                  key={
                    name
                  }
                  value={
                    name
                  }
                >
                  {
                    name
                  }
                </option>
              )
            )}
          </select>

          <button
            type="button"
            className="primary-btn"
            onClick={
              exportRows
            }
            disabled={
              filtered.length ===
              0
            }
            style={{
              height:
                "42px",

              minWidth:
                "100px",

              padding:
                "0 18px",

              whiteSpace:
                "nowrap",
            }}
          >
            Export
          </button>
        </div>
      </div>

      {/* ========================================================
          LOADING / ERROR
      ======================================================== */}

      {loading ? (
        <div className="empty">
          Loading QC pending GRNs...
        </div>
      ) : error ? (
        <div className="error-banner">
          {
            error
          }
        </div>
      ) : (
        <>
          {/* ====================================================
              FRONT TABLE
          ==================================================== */}

          <div className="role-table-scroll">
            <table className="role-table">
              <thead>
                <tr>
                  <th>
                    Vendor
                  </th>

                  <th>
                    Oracle Vendor ID
                  </th>

                  <th>
                    PO Number
                  </th>

                  <th>
                    PR Number
                  </th>

                  <th>
                    Item Description
                  </th>

                  <th>
                    QC Status
                  </th>

                  <th>
                    Action
                  </th>
                </tr>
              </thead>

              <tbody>
                {visibleRows.map(
                  (
                    row,
                    index
                  ) => (
                    <tr
                      key={
                        `${row.oracleVendorId}-${row.poNumber}-${row.prNumber || ""}-${row.poLineNumber || index}`
                      }
                      onDoubleClick={() =>
                        openDetails(
                          row
                        )
                      }
                      title="Double-click to view details"
                      style={{
                        cursor:
                          "pointer",
                      }}
                    >
                      <td>
                        <strong>
                          {
                            row.vendorName
                          }
                        </strong>
                      </td>

                      <td>
                        {
                          row.oracleVendorId
                        }
                      </td>

                      <td>
                        {
                          row.poNumber
                        }
                      </td>

                      <td>
                        {displayValue(
                          row.prNumber
                        )}
                      </td>

                      <td
                        title={
                          row.itemDescription
                          ||
                          ""
                        }
                        style={{
                          maxWidth:
                            "390px",

                          whiteSpace:
                            "normal",
                        }}
                      >
                        {
                          row.itemDescription
                          ||
                          row.itemCode
                          ||
                          "-"
                        }
                      </td>

                      <td>
                        <span className="role-status orange">
                          Pending with QC
                        </span>
                      </td>

                      <td>
                        <button
                          type="button"
                          className="table-btn"
                          onClick={
                            (
                              event
                            ) => {
                              event.stopPropagation();

                              openDetails(
                                row
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

                {visibleRows.length ===
                0 ? (
                  <tr>
                    <td
                      colSpan={
                        7
                      }
                      className="empty"
                    >
                      No QC pending GRNs found for active vendors.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>

          {/* ====================================================
              PAGINATION
          ==================================================== */}

          <div className="role-pagination">
            <span>
              Showing{" "}
              {
                visibleRows.length
              }{" "}
              of{" "}
              {
                filtered.length
              }{" "}
              QC pending record
              {filtered.length ===
              1
                ? ""
                : "s"}
            </span>

            <div>
              <button
                type="button"
                disabled={
                  safePage <=
                  1
                }
                onClick={() =>
                  setPage(
                    (
                      current
                    ) =>
                      Math.max(
                        1,

                        current -
                        1
                      )
                  )
                }
              >
                &lt;
              </button>

              <em>
                {
                  safePage
                }{" "}
                /{" "}
                {
                  totalPages
                }
              </em>

              <button
                type="button"
                disabled={
                  safePage >=
                  totalPages
                }
                onClick={() =>
                  setPage(
                    (
                      current
                    ) =>
                      Math.min(
                        totalPages,

                        current +
                        1
                      )
                  )
                }
              >
                &gt;
              </button>
            </div>
          </div>
        </>
      )}

      {/* ========================================================
          VIEW DETAILS MODAL
      ======================================================== */}

      {selectedRow && (
        <div
          role="presentation"
          onMouseDown={
            (
              event
            ) => {
              if (
                event.target ===
                event.currentTarget
              ) {
                closeDetails();
              }
            }
          }
          style={{
            position:
              "fixed",

            inset:
              0,

            zIndex:
              9999,

            display:
              "flex",

            alignItems:
              "center",

            justifyContent:
              "center",

            padding:
              "24px",

            background:
              "rgba(15, 23, 42, 0.48)",

            backdropFilter:
              "blur(2px)",
          }}
        >
          <section
            role="dialog"
            aria-modal="true"
            aria-label="QC Pending GRN Details"
            style={{
              width:
                "min(900px, 96vw)",

              maxHeight:
                "90vh",

              overflowY:
                "auto",

              background:
                "#ffffff",

              borderRadius:
                "16px",

              boxShadow:
                "0 24px 70px rgba(15, 23, 42, 0.28)",

              border:
                "1px solid #e4e9e7",
            }}
          >
            {/* ==================================================
                MODAL HEADER
            ================================================== */}

            <div
              style={{
                display:
                  "flex",

                alignItems:
                  "flex-start",

                justifyContent:
                  "space-between",

                gap:
                  "20px",

                padding:
                  "22px 24px",

                borderBottom:
                  "1px solid #e4e9e7",
              }}
            >
              <div>
                <h3
                  style={{
                    margin:
                      0,

                    fontSize:
                      "20px",
                  }}
                >
                  QC Pending Details
                </h3>

                <p
                  style={{
                    margin:
                      "6px 0 0",

                    color:
                      "#667085",

                    fontSize:
                      "14px",
                  }}
                >
                  Complete PO, PR, GRN, item and QC information.
                </p>
              </div>

              <button
                type="button"
                onClick={
                  closeDetails
                }
                aria-label="Close"
                style={{
                  width:
                    "38px",

                  height:
                    "38px",

                  display:
                    "grid",

                  placeItems:
                    "center",

                  border:
                    "1px solid #d7dfdc",

                  borderRadius:
                    "8px",

                  background:
                    "#ffffff",

                  cursor:
                    "pointer",

                  fontSize:
                    "22px",

                  lineHeight:
                    1,
                }}
              >
                ×
              </button>
            </div>

            {/* ==================================================
                DETAILS
            ================================================== */}

            <div
              style={{
                padding:
                  "24px",
              }}
            >
              <div
                style={{
                  display:
                    "grid",

                  gridTemplateColumns:
                    "repeat(2, minmax(0, 1fr))",

                  gap:
                    "14px",
                }}
              >
                <DetailField
                  label="Vendor Name"
                  value={
                    selectedRow.vendorName
                  }
                />

                <DetailField
                  label="Vendor Code"
                  value={
                    displayValue(
                      selectedRow.vendorCode
                    )
                  }
                />

                <DetailField
                  label="Oracle Vendor ID"
                  value={
                    selectedRow.oracleVendorId
                  }
                />

                <DetailField
                  label="PO Number"
                  value={
                    selectedRow.poNumber
                  }
                />

                {/* NEW */}
                <DetailField
                  label="PO Date"
                  value={
                    formatDate(
                      selectedRow.poDate
                    )
                  }
                />

                <DetailField
                  label="PR Number"
                  value={
                    displayValue(
                      selectedRow.prNumber
                    )
                  }
                />

                <DetailField
                  label="PO Line Number"
                  value={
                    displayValue(
                      selectedRow.poLineNumber
                    )
                  }
                />

                <DetailField
                  label="GRN Number"
                  value={
                    displayValue(
                      selectedRow.grnNumber
                    )
                  }
                />

                <DetailField
                  label="GRN Date"
                  value={
                    formatDate(
                      selectedRow.grnDate
                    )
                  }
                />

                <DetailField
                  label="Item Code"
                  value={
                    displayValue(
                      selectedRow.itemCode
                    )
                  }
                />

                <DetailField
                  label="UOM"
                  value={
                    displayValue(
                      selectedRow.uom
                    )
                  }
                />

                <DetailField
                  label="Received Quantity"
                  value={
                    formatQuantity(
                      selectedRow.receivedQuantity
                    )
                  }
                />

                <DetailField
                  label="QC Status"
                  value={
                    selectedRow.qcStatus
                  }
                />

                <DetailField
                  label="Oracle QC Status"
                  value={
                    displayValue(
                      selectedRow.oracleQcStatus
                    )
                  }
                />

                <DetailField
                  label="Aging"
                  value={
                    `${selectedRow.agingDays} day${
                      selectedRow.agingDays ===
                      1
                        ? ""
                        : "s"
                    }`
                  }
                />

                <div
                  style={{
                    gridColumn:
                      "1 / -1",
                  }}
                >
                  <DetailField
                    label="Item Description"
                    value={
                      displayValue(
                        selectedRow.itemDescription
                      )
                    }
                  />
                </div>
              </div>
            </div>

            {/* ==================================================
                FOOTER
            ================================================== */}

            <div
              style={{
                display:
                  "flex",

                justifyContent:
                  "flex-end",

                padding:
                  "16px 24px",

                borderTop:
                  "1px solid #e4e9e7",
              }}
            >
              <button
                type="button"
                className="primary-btn"
                onClick={
                  closeDetails
                }
              >
                Close
              </button>
            </div>
          </section>
        </div>
      )}
    </section>
  );
}

// ============================================================
// DETAIL FIELD
// ============================================================

function DetailField({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div
      style={{
        padding:
          "14px 16px",

        border:
          "1px solid #e4e9e7",

        borderRadius:
          "10px",

        background:
          "#fbfcfc",

        minHeight:
          "72px",

        boxSizing:
          "border-box",
      }}
    >
      <span
        style={{
          display:
            "block",

          marginBottom:
            "6px",

          color:
            "#7a8699",

          fontSize:
            "12px",

          fontWeight:
            500,
        }}
      >
        {
          label
        }
      </span>

      <strong
        style={{
          display:
            "block",

          color:
            "#162033",

          fontSize:
            "14px",

          lineHeight:
            1.45,

          wordBreak:
            "break-word",
        }}
      >
        {
          value
        }
      </strong>
    </div>
  );
}