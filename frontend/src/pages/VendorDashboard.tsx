import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Icon } from "../components/Icons";
import {
  getMyOracleInvoices,
  getMyPoGrns,
  getMySupplier,
  getMyPortalInvoices,
} from "../api/portal";
import type {
  OracleInvoice,
  OraclePoGrn,
  OracleSupplier,
  PortalInvoice,
} from "../types";
import {
  formatPakistanFiscalWindowStart,
  isInPakistanFiscalWindow,
} from "../utils/pakistanFiscalWindow";

const money = (v: number | null | undefined) =>
  `PKR ${Number(v || 0).toLocaleString()}`;

const qcLabel = (s?: string | null) => {
  const x = (s || "").toUpperCase();

  if (
    [
      "PENDING",
      "PENDING QC",
      "PENDING_QC",
      "AWAITING INSPECTION",
      "NOT RECEIVED",
    ].includes(x)
  ) {
    return "Pending with QC";
  }

  if (x === "ACCEPTED") return "Available";
  if (x === "REJECTED") return "Rejected";

  return s || "Not Required";
};

const statusClass = (s?: string | null) => {
  const x = (s || "").toUpperCase();

  if (
    x.includes("PAID") ||
    x.includes("ACCEPT") ||
    x.includes("APPROV") ||
    x.includes("AVAILABLE") ||
    x.includes("SUCCESS")
  ) {
    return "green";
  }

  if (
    x.includes("RETURN") ||
    x.includes("REJECT") ||
    x.includes("CANCEL")
  ) {
    return "red";
  }

  if (
    x.includes("PENDING") ||
    x.includes("NOT RECEIVED")
  ) {
    return "orange";
  }

  return "blue";
};

export function VendorDashboard() {
  const [supplier, setSupplier] =
    useState<OracleSupplier | null>(null);

  const [rows, setRows] =
    useState<OraclePoGrn[]>([]);

  const [invoices, setInvoices] =
    useState<OracleInvoice[]>([]);

  const [portalInvoices, setPortalInvoices] =
    useState<PortalInvoice[]>([]);

  const [error, setError] =
    useState("");

  useEffect(() => {
    Promise.all([
      getMySupplier(),
      getMyPoGrns(),
      getMyOracleInvoices(),
      getMyPortalInvoices(),
    ])
      .then(([s, p, i, pi]) => {
        setSupplier(s);
        setRows(p);
        setInvoices(i);
        setPortalInvoices(pi);
      })
      .catch((e) =>
        setError(
          e?.response?.data?.message ||
            e.message
        )
      );
  }, []);

  const fiscalNow = useMemo(() => new Date(), []);

  const visibleRows = useMemo(
    () =>
      rows.filter((x) =>
        isInPakistanFiscalWindow(
          x.receiptDate ||
            x.poCreationDate ||
            x.poApprovedDate,
          fiscalNow
        )
      ),
    [fiscalNow, rows]
  );

  const dashboardInvoices = useMemo(
    () =>
      invoices.filter((x) =>
        isInPakistanFiscalWindow(
          x.invoiceDate,
          fiscalNow
        )
      ),
    [fiscalNow, invoices]
  );

  const dashboardPortalInvoices = useMemo(
    () =>
      portalInvoices.filter((x) =>
        isInPakistanFiscalWindow(
          x.submissionDate || x.invoiceDate,
          fiscalNow
        )
      ),
    [fiscalNow, portalInvoices]
  );

  const poMap = useMemo(
    () =>
      new Map(
        visibleRows
          .filter((x) => x.poNumber)
          .map((x) => [
            x.poNumber,
            x,
          ])
      ),
    [visibleRows]
  );

  const grns = visibleRows.filter(
    (x) =>
      x.grnNumber &&
      (x.quantityAvailableToInvoice || 0) > 0 &&
      ![
        "PENDING",
        "PENDING QC",
        "PENDING_QC",
        "AWAITING INSPECTION",
        "NOT RECEIVED",
        "REJECTED",
      ].includes((x.inspectionStatus || "").toUpperCase())
  );

  const availableGrnNumbers = new Set(
    grns.map((x) => x.grnNumber)
  );

  const submittedKeys =
    new Set(
      dashboardInvoices.map((x) =>
        x.invoiceNumber.toLowerCase()
      )
    );

  const pendingPortal =
    dashboardPortalInvoices.filter(
      (x) =>
        !submittedKeys.has(
          (
            x.invoiceNumber || ""
          ).toLowerCase()
        )
    );

  const paid =
    dashboardInvoices.filter((x) =>
      (
        x.paymentStatus || ""
      )
        .toUpperCase()
        .includes("PAID")
    );

  const returned =
    dashboardInvoices.filter((x) =>
      (
        x.approvalStatus || ""
      )
        .toUpperCase()
        .includes("RETURN")
    );

  const cancelled =
    dashboardInvoices.filter((x) =>
      (
        x.approvalStatus || ""
      )
        .toUpperCase()
        .includes("CANCEL")
    );

  const paidAmt =
    paid.reduce(
      (a, x) =>
        a + (x.amountPaid || 0),
      0
    );

  const totalInv =
    dashboardInvoices.reduce(
      (a, x) =>
        a + x.invoiceAmount,
      0
    );

  return (
    <div className="dashboard-v2">
      {error && (
        <div className="error-banner">
          {error}
        </div>
      )}

      <div className="last-update">
        Last updated:{" "}
        {new Date().toLocaleString()}
        <span className="vendor-data-window">
          Data from {formatPakistanFiscalWindowStart()}
        </span>
      </div>

      <section className="kpi-grid">
        {[
          [
            "POs Outstanding",
            poMap.size,
            money(
              [...poMap.values()].reduce(
                (a, x) =>
                  a +
                  (x.poLineAmount || 0),
                0
              )
            ),
            "po",
            "green",
          ],
          [
            "GRNs Available",
            availableGrnNumbers.size,
            money(
              grns.reduce(
                (a, x) =>
                  a +
                  (x.quantityAvailableToInvoice ??
                    x.grnReceivedQuantity ??
                    0) *
                    (x.unitPrice || 0),
                0
              )
            ),
            "grn",
            "blue",
          ],
          [
            "Invoices Submitted",
            dashboardInvoices.length +
              pendingPortal.filter(
                (x) =>
                  x.status !==
                  "DRAFT"
              ).length,
            money(
              totalInv +
                pendingPortal.reduce(
                  (a, x) =>
                    a +
                    x.invoiceAmount,
                  0
                )
            ),
            "invoice",
            "purple",
          ],
          [
            "Invoices Paid",
            paid.length,
            money(paidAmt),
            "payment",
            "teal",
          ],
          [
            "Invoices Returned",
            returned.length,
            money(
              returned.reduce(
                (a, x) =>
                  a +
                  x.invoiceAmount,
                0
              )
            ),
            "invoice",
            "orange",
          ],
          [
            "Invoices Cancelled",
            cancelled.length,
            money(
              cancelled.reduce(
                (a, x) =>
                  a +
                  x.invoiceAmount,
                0
              )
            ),
            "support",
            "red",
          ],
        ].map(
          ([l, n, a, i, c]) => (
            <div
              className={`kpi-v2 kpi-${c}`}
              key={String(l)}
            >
              <span
                className={`kpi-round ${c}`}
              >
                <Icon
                  name={String(i)}
                  size={20}
                />
              </span>

              <div className="kpi-copy">
                <small>{l}</small>
                <strong>{n}</strong>
                <p>{a}</p>
              </div>
            </div>
          )
        )}
      </section>

      <section className="dashboard-row top-row">
        <div className="panel invoice-overview">
          <h3>
            Invoice Status Overview
          </h3>

          <div className="overview-body">
            <div className="donut">
              <div>
                <strong>
                  {dashboardInvoices.length}
                </strong>

                <span>
                  Total Invoices
                </span>
              </div>
            </div>

            <ul>
              {[
                [
                  "Submitted",
                  dashboardInvoices.filter((x) =>
                    (
                      x.approvalStatus ||
                      ""
                    )
                      .toUpperCase()
                      .includes(
                        "SUBMIT"
                      )
                  ).length,
                ],
                [
                  "Pending Finance",
                  dashboardInvoices.filter((x) =>
                    (
                      x.approvalStatus ||
                      ""
                    )
                      .toUpperCase()
                      .includes("PEND")
                  ).length,
                ],
                [
                  "Returned",
                  returned.length,
                ],
                [
                  "Paid",
                  paid.length,
                ],
                [
                  "Cancelled",
                  cancelled.length,
                ],
              ].map(([l, n]) => (
                <li key={String(l)}>
                  <i></i>
                  <span>{l}</span>
                  <b>{n}</b>
                </li>
              ))}
            </ul>
          </div>

          <Link to="/invoices">
            View Invoice History →
          </Link>
        </div>

        {/* =========================================
            RECENT INVOICES
            Oracle Integration replaced with
            Payment Status
        ========================================== */}

        <div className="panel recent">
          <div className="panel-title">
            <h3>
              Recent Invoices
            </h3>

            <Link to="/invoices">
              View All →
            </Link>
          </div>

          <table>
            <thead>
              <tr>
                <th>
                  Invoice #
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
                  Payment Status
                </th>
              </tr>
            </thead>

            <tbody>
              {dashboardInvoices
                .slice(0, 5)
                .map((x) => (
                  <tr
                    key={
                      x.invoiceNumber
                    }
                  >
                    <td>
                      {
                        x.invoiceNumber
                      }
                    </td>

                    <td>
                      {x.invoiceDate ||
                        "-"}
                    </td>

                    <td>
                      {Number(
                        x.invoiceAmount
                      ).toLocaleString()}
                    </td>

                    <td>
                      <span
                        className={`status ${statusClass(
                          x.approvalStatus
                        )}`}
                      >
                        {x.approvalStatus ||
                          "Submitted"}
                      </span>
                    </td>

                    <td>
                      <span
                        className={`status ${statusClass(
                          x.paymentStatus
                        )}`}
                      >
                        {x.paymentStatus ||
                          "Pending"}
                      </span>
                    </td>
                  </tr>
                ))}

              {!dashboardInvoices.length && (
                <tr>
                  <td
                    colSpan={5}
                    className="empty"
                  >
                    No Oracle invoice
                    activity yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section className="dashboard-row triple">
        <div className="panel">
          <div className="panel-title">
            <h3>
              Purchase Orders
            </h3>

            <Link to="/purchase-orders">
              View All →
            </Link>
          </div>

          <table>
            <thead>
              <tr>
                <th>
                  PO Number
                </th>

                <th>
                  Status
                </th>

                <th>
                  Total Amount
                </th>

                <th>
                  Open Amount
                </th>
              </tr>
            </thead>

            <tbody>
              {[...poMap.values()]
                .slice(0, 5)
                .map((x) => (
                  <tr
                    key={x.poNumber}
                  >
                    <td>
                      {x.poNumber}
                    </td>

                    <td>
                      <span
                        className={`status ${statusClass(
                          x.poStatus
                        )}`}
                      >
                        {x.poStatus ||
                          "Open"}
                      </span>
                    </td>

                    <td>
                      {Number(
                        x.poLineAmount ||
                          0
                      ).toLocaleString()}
                    </td>

                    <td>
                      {Number(
                        x.quantityAvailableToInvoice ||
                          0
                      ).toLocaleString()}
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>

        <div className="panel">
          <div className="panel-title">
            <h3>
              GRNs
            </h3>

            <Link to="/purchase-orders">
              View All →
            </Link>
          </div>

          <table>
            <thead>
              <tr>
                <th>
                  GRN Number
                </th>

                <th>
                  PO Number
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
              {grns
                .slice(0, 5)
                .map(
                  (
                    x,
                    i
                  ) => (
                    <tr
                      key={`${x.grnNumber}-${i}`}
                    >
                      <td>
                        {
                          x.grnNumber
                        }
                      </td>

                      <td>
                        {
                          x.poNumber
                        }
                      </td>

                      <td>
                        {x.quantityAvailableToInvoice ??
                          x.grnReceivedQuantity ??
                          0}
                      </td>

                      <td>
                        <span
                          className={`status ${statusClass(
                            qcLabel(
                              x.inspectionStatus
                            )
                          )}`}
                        >
                          {qcLabel(
                            x.inspectionStatus
                          )}
                        </span>
                      </td>
                    </tr>
                  )
                )}
            </tbody>
          </table>
        </div>

        <div className="panel create-card">
          <div className="doc-plus">
            ＋
          </div>

          <h3>
            Submit Invoice
          </h3>

          <p>
            Submit a new invoice
            against your eligible
            POs and GRNs.
          </p>

          <Link
            className="primary-btn"
            to="/invoices/new"
          >
            ＋ Submit Invoice
          </Link>
        </div>
      </section>

      <section className="dashboard-row bottom-row">
        <div className="panel">
          <div className="panel-title">
            <h3>
              Payments
            </h3>

            <Link to="/payments">
              View All →
            </Link>
          </div>

          <div className="mini-kpis">
            <div>
              <small>
                Total Paid
              </small>

              <b>
                {money(paidAmt)}
              </b>
            </div>

            <div>
              <small>
                Pending
              </small>

              <b>
                {money(
                  dashboardInvoices.reduce(
                    (a, x) =>
                      a +
                      (x.outstandingAmount ||
                        0),
                    0
                  )
                )}
              </b>
            </div>
          </div>

          <table>
            <thead>
              <tr>
                <th>
                  Invoice #
                </th>

                <th>
                  Invoice Amount
                </th>

                <th>
                  Paid Amount
                </th>

                <th>
                  Status
                </th>
              </tr>
            </thead>

            <tbody>
              {dashboardInvoices
                .slice(0, 5)
                .map((x) => (
                  <tr
                    key={
                      x.invoiceNumber
                    }
                  >
                    <td>
                      {
                        x.invoiceNumber
                      }
                    </td>

                    <td>
                      {Number(
                        x.invoiceAmount
                      ).toLocaleString()}
                    </td>

                    <td>
                      {Number(
                        x.amountPaid ||
                          0
                      ).toLocaleString()}
                    </td>

                    <td>
                      <span
                        className={`status ${statusClass(
                          x.paymentStatus
                        )}`}
                      >
                        {x.paymentStatus ||
                          "Pending"}
                      </span>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>

        <div className="panel profile-card">
          <h3>
            Vendor Profile
          </h3>

          <dl>
            <dt>
              Company
            </dt>
            <dd>
              {supplier?.vendorName ||
                "-"}
            </dd>

            <dt>
              Supplier #
            </dt>
            <dd>
              {supplier?.supplierNumber ||
                "-"}
            </dd>

            <dt>
              Vendor ID
            </dt>
            <dd>
              {supplier?.vendorId ||
                "-"}
            </dd>

            <dt>
              Site
            </dt>
            <dd>
              {supplier?.vendorSiteCode ||
                "-"}
            </dd>

            <dt>
              Status
            </dt>
            <dd>
              Active
            </dd>
          </dl>

          <Link
            to="/vendor-profile"
            className="secondary-btn"
          >
            View Profile →
          </Link>
        </div>
      </section>
    </div>
  );
}
