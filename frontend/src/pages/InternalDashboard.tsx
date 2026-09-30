import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Icon } from "../components/Icons";
import { useAuth } from "../auth/AuthContext";
import { getLiveDashboard } from "../api/portal";
import { getQcPendingGrns } from "../api/supplyChain";
import type { QcPendingGrn } from "../api/supplyChain";

type DashboardData = any;
type TabKey = string;

function formatDate(value?: string | null) {
  if (!value) return "-";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}
function formatMoney(value?: number | null) { return Number(value || 0).toLocaleString(); }
function pct(part: number, total: number) { return total ? `${((part / total) * 100).toFixed(1)}%` : "0.0%"; }
function StatusPill({ value }: { value: string }) {
  const v = (value || "-").toLowerCase();
  const cls =
    v.includes("reject") || v.includes("fail") || v.includes("cancel") || v.includes("disabled")
      ? "red"
      : v.includes("processing") || v.includes("review") || v.includes("partial") || v.includes("not invoiced")
        ? "blue"
        : "green";
  return <span className={`role-status ${cls}`}>{value || "-"}</span>;
}
function TrendBars({ rows, leftLabel, rightLabel }: { rows: any[]; leftLabel: string; rightLabel: string }) {
  const leftKey = leftLabel === "Purchase Orders" ? "purchaseOrders" : "submitted";
  const rightKey = rightLabel === "GRNs" ? "grns" : rightLabel === "Approved" ? "approved" : "integrated";
  const max = Math.max(1, ...rows.flatMap(r => [Number(r[leftKey] || 0), Number(r[rightKey] || 0)]));
  return <div className="role-trend-chart">
    <div className="role-chart-legend"><span><i className="light" />{leftLabel}</span><span><i className="dark" />{rightLabel}</span></div>
    <div className="role-bars">{rows.map((r) => <div className="role-bar-group" key={r.month}>
      <div className="role-bar-pair"><b style={{ height: `${Math.max(4, Number(r[leftKey] || 0) / max * 145)}px` }} /><b className="dark" style={{ height: `${Math.max(4, Number(r[rightKey] || 0) / max * 145)}px` }} /></div><span>{r.month}</span>
    </div>)}</div>
  </div>;
}
function ApprovalTurnaroundChart({ rows }: { rows: any[] }) {
  const [period, setPeriod] = useState<3 | 6 | 12>(6);

  const chartRows = useMemo(() => {
    const allRows = Array.isArray(rows) ? rows : [];
    return allRows.slice(-period);
  }, [rows, period]);

  const width = 560;
  const height = 240;
  const left = 52;
  const right = 20;
  const top = 32;
  const bottom = 44;
  const plotWidth = width - left - right;
  const plotHeight = height - top - bottom;

  const numericValues = chartRows
    .map((row) => row?.averageDays)
    .filter((value) => value !== null && value !== undefined)
    .map((value) => Number(value))
    .filter((value) => Number.isFinite(value));

  const dataMax = numericValues.length ? Math.max(...numericValues) : 0;
  const yMax = Math.max(6, Math.ceil(dataMax));
  const yTicks = Array.from({ length: 7 }, (_, index) => index);

  const points = chartRows.map((row, index) => {
    const x = chartRows.length <= 1
      ? left + plotWidth / 2
      : left + (index / (chartRows.length - 1)) * plotWidth;

    const rawValue = row?.averageDays;
    const hasData =
      Number(row?.approvedCount || 0) > 0 &&
      rawValue !== null &&
      rawValue !== undefined &&
      Number.isFinite(Number(rawValue));

    const value = hasData ? Number(rawValue) : null;
    const y = value === null
      ? top + plotHeight
      : top + plotHeight - (value / yMax) * plotHeight;

    return { x, y, value, row, hasData };
  });

  const lineSegments: string[] = [];
  let currentSegment: string[] = [];

  points.forEach((point) => {
    if (!point.hasData) {
      if (currentSegment.length) {
        lineSegments.push(currentSegment.join(" "));
        currentSegment = [];
      }
      return;
    }

    currentSegment.push(
      `${currentSegment.length === 0 ? "M" : "L"} ${point.x} ${point.y}`
    );
  });

  if (currentSegment.length) {
    lineSegments.push(currentSegment.join(" "));
  }

  const activePoints = points.filter((point) => point.hasData);
  const canFillArea = activePoints.length > 1 && activePoints.length === points.length;
  const areaPath = canFillArea
    ? `${lineSegments[0]} L ${activePoints[activePoints.length - 1].x} ${top + plotHeight} L ${activePoints[0].x} ${top + plotHeight} Z`
    : "";

  const label = `Last ${period} Months`;

  return (
    <div style={{ width: "100%", paddingTop: 2 }}>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "flex-start",
          gap: 12,
          marginBottom: 4,
        }}
      >
        <div>
          <h3 style={{ margin: 0 }}>Average Approval Turnaround Time</h3>
          <p style={{ margin: "3px 0 0", fontSize: 12, color: "#64748b" }}>
            Average days from submission to approval ({label})
          </p>
        </div>

        <div
          style={{
            position: "relative",
            display: "inline-flex",
            alignItems: "center",
            minWidth: 142,
            height: 36,
            border: "1px solid #dbe3df",
            borderRadius: 8,
            background: "#ffffff",
            color: "#334155",
            boxShadow: "0 1px 2px rgba(15, 23, 42, 0.04)",
          }}
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
            style={{ position: "absolute", left: 10, pointerEvents: "none" }}
          >
            <rect x="3" y="5" width="18" height="16" rx="2" />
            <path d="M16 3v4M8 3v4M3 11h18" />
          </svg>

          <select
            value={period}
            onChange={(event) => setPeriod(Number(event.target.value) as 3 | 6 | 12)}
            aria-label="Approval turnaround period"
            style={{
              width: "100%",
              height: "100%",
              border: 0,
              outline: 0,
              appearance: "none",
              WebkitAppearance: "none",
              background: "transparent",
              padding: "0 30px 0 34px",
              fontSize: 11.5,
              fontWeight: 500,
              color: "#334155",
              cursor: "pointer",
            }}
          >
            <option value={3}>Last 3 Months</option>
            <option value={6}>Last 6 Months</option>
            <option value={12}>Last 12 Months</option>
          </select>

          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
            style={{ position: "absolute", right: 9, pointerEvents: "none" }}
          >
            <path d="m6 9 6 6 6-6" />
          </svg>
        </div>
      </div>

      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={`Average approval turnaround time during the last ${period} months`}
        style={{ width: "100%", height: 220, display: "block", overflow: "visible" }}
      >
        <defs>
          <linearGradient id="approvalTurnaroundFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#16a34a" stopOpacity="0.20" />
            <stop offset="100%" stopColor="#16a34a" stopOpacity="0.02" />
          </linearGradient>
        </defs>

        {yTicks.map((tick) => {
          const value = (yMax / 6) * tick;
          const y = top + plotHeight - (value / yMax) * plotHeight;
          return (
            <g key={tick}>
              <line
                x1={left}
                x2={width - right}
                y1={y}
                y2={y}
                stroke="#edf2ef"
                strokeWidth="1"
              />
              <text
                x={left - 10}
                y={y + 4}
                textAnchor="end"
                fontSize="10"
                fill="#64748b"
              >
                {Number.isInteger(value) ? value : value.toFixed(1)}
              </text>
            </g>
          );
        })}

        <line
          x1={left}
          x2={left}
          y1={top}
          y2={top + plotHeight}
          stroke="#cbd5e1"
          strokeWidth="1"
        />
        <line
          x1={left}
          x2={width - right}
          y1={top + plotHeight}
          y2={top + plotHeight}
          stroke="#cbd5e1"
          strokeWidth="1"
        />

        <text
          x="14"
          y={top + plotHeight / 2}
          transform={`rotate(-90 14 ${top + plotHeight / 2})`}
          textAnchor="middle"
          fontSize="10"
          fill="#475569"
        >
          Days
        </text>

        {areaPath && <path d={areaPath} fill="url(#approvalTurnaroundFill)" />}

        {lineSegments.map((path, index) => (
          <path
            key={index}
            d={path}
            fill="none"
            stroke="#16a34a"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        ))}

        {points.map((point, index) => (
          <g key={`${point.row.month}-${index}`}>
            {point.hasData && (
              <>
                <circle cx={point.x} cy={point.y} r="4.5" fill="#16a34a" />
                <circle cx={point.x} cy={point.y} r="9" fill="transparent">
                  <title>
                    {point.row.month}: {point.value!.toFixed(1)} days ({point.row.approvedCount} approved)
                  </title>
                </circle>
                <text
                  x={point.x}
                  y={Math.max(14, point.y - 11)}
                  textAnchor="middle"
                  fontSize="10.5"
                  fontWeight="700"
                  fill="#0f172a"
                >
                  {point.value!.toFixed(1)}
                </text>
              </>
            )}

            <text
              x={point.x}
              y={top + plotHeight + 23}
              textAnchor="middle"
              fontSize="10.5"
              fill="#64748b"
            >
              {point.row.month}
            </text>
          </g>
        ))}

        {activePoints.length === 0 && (
          <text
            x={left + plotWidth / 2}
            y={top + plotHeight / 2}
            textAnchor="middle"
            fontSize="12"
            fill="#94a3b8"
          >
            No approved invoices in this period.
          </text>
        )}
      </svg>
    </div>
  );
}

function VendorBars({ title, rows }: { title: string; rows: any[] }) {
  const max = Math.max(1, ...rows.map(r => Number(r.amount || 0)));
  return <div className="role-panel"><h3>{title}</h3><div className="role-vendor-bars">
    {rows.length ? rows.map(r => <div className="role-vendor-row" key={`${r.vendorId}-${r.vendorName}`}><span>{r.vendorName}</span><div><b style={{ width: `${Number(r.amount || 0) / max * 100}%` }} /></div><strong>PKR {formatMoney(r.amount)}</strong></div>) : <p className="empty">No live vendor data available.</p>}
  </div></div>;
}
function Kpi({ icon, label, value, tone }: { icon: string; label: string; value: number; tone: string }) {
  return <div className={`role-kpi role-kpi-card-${tone}`}><span className={`role-kpi-icon ${tone}`}><Icon name={icon} size={24} /></span><div><small>{label}</small><strong>{Number(value || 0).toLocaleString()}</strong></div></div>;
}
function Pager({ page, pages, total, pageSize, onPage }: { page: number; pages: number; total: number; pageSize: number; onPage: (n:number)=>void }) {
  const start = total ? (page - 1) * pageSize + 1 : 0, end = Math.min(total, page * pageSize);
  const buttons = Array.from({length: Math.min(5, pages)}, (_,i) => Math.min(Math.max(1, page - 2) + i, pages)).filter((n,i,a)=>a.indexOf(n)===i);
  return <div className="role-pagination"><span>Showing {start} to {end} of {total.toLocaleString()} records</span><div><button disabled={page<=1} onClick={()=>onPage(page-1)}>‹</button>{buttons.map(n=><button key={n} className={n===page?"active":""} onClick={()=>onPage(n)}>{n}</button>)}{pages>5&&<><em>…</em><button onClick={()=>onPage(pages)}>{pages}</button></>}<button disabled={page>=pages} onClick={()=>onPage(page+1)}>›</button></div></div>;
}

function FinanceDashboard({ data }: { data: any }) {
  const [tab, setTab] = useState<TabKey>("recent");

  const rows: any[] = data.invoices || [];
  const statusData = data.status || {};

  const pending = Number(statusData.pending || 0);
  const approved = Number(statusData.approved || 0);
  const rejected = Number(statusData.rejected || 0);
  const cancelled = Number(statusData.cancelled || 0);
  const paid = Number(statusData.paid || 0);
  const total = Number(statusData.total || 0);

  const tabRows = useMemo(
    () =>
      rows.filter(
        (r: any) =>
          tab === "recent" ||
          (tab === "pending" && r.status === "Pending for Approval") ||
          (tab === "approved" && r.status === "Approved") ||
          (tab === "rejected" && r.status === "Rejected") ||
          (tab === "cancelled" && r.status === "Cancelled") ||
          (tab === "paid" && r.status === "Paid")
      ),
    [rows, tab]
  );

  // Main Finance dashboard intentionally shows only the latest 10 rows.
  // Full history is available from the dedicated Invoice Records page.
  const visible = tabRows.slice(0, 10);

  const representedTotal = pending + approved + rejected + cancelled + paid;
  const donutTotal = Math.max(1, representedTotal);
  const pendingEnd = (pending / donutTotal) * 100;
  const approvedEnd = pendingEnd + (approved / donutTotal) * 100;
  const rejectedEnd = approvedEnd + (rejected / donutTotal) * 100;
  const cancelledEnd = rejectedEnd + (cancelled / donutTotal) * 100;

  const donutStyle = {
    background: `conic-gradient(#22c55e 0 ${pendingEnd}%, #16a34a ${pendingEnd}% ${approvedEnd}%, #ef4444 ${approvedEnd}% ${rejectedEnd}%, #94a3b8 ${rejectedEnd}% ${cancelledEnd}%, #2563eb ${cancelledEnd}% 100%)`
  };

  return <div className="role-dashboard finance-dashboard">
    <section className="role-kpis" style={{ gridTemplateColumns: "repeat(6, minmax(0, 1fr))" }}>
      <Kpi icon="invoice" label="Total Invoices" value={data.kpis?.totalInvoices} tone="green"/>
      <Kpi icon="history" label="Pending for Approval" value={data.kpis?.pendingForApproval} tone="orange"/>
      <Kpi icon="admin" label="Approved" value={data.kpis?.approved} tone="blue"/>
      <Kpi icon="support" label="Rejected" value={data.kpis?.rejected} tone="red"/>
      <Kpi icon="invoice" label="Cancelled" value={data.kpis?.cancelled} tone="purple"/>
      <Kpi icon="payment" label="Paid" value={data.kpis?.paid} tone="green"/>
    </section>

    <section className="role-analytics-grid">
      <div className="role-panel">
        <h3>Invoice Status</h3>
        <div className="role-donut-wrap">
          <div className="role-donut" style={donutStyle}><div><strong>{total.toLocaleString()}</strong><span>Total</span></div></div>
          <ul>
            <li><i className="g"/>Pending for Approval <b>{pending.toLocaleString()} ({pct(pending,total)})</b></li>
            <li><i className="b"/>Approved <b>{approved.toLocaleString()} ({pct(approved,total)})</b></li>
            <li><i className="r"/>Rejected <b>{rejected.toLocaleString()} ({pct(rejected,total)})</b></li>
            <li><i className="gray"/>Cancelled <b>{cancelled.toLocaleString()} ({pct(cancelled,total)})</b></li>
            <li><i className="p"/>Paid <b>{paid.toLocaleString()} ({pct(paid,total)})</b></li>
          </ul>
        </div>
      </div>
      <div className="role-panel">
        <ApprovalTurnaroundChart rows={data.approvalTurnaroundTrend || []}/>
      </div>
      <VendorBars title="Top Vendors (by Invoice Amount)" rows={data.topVendors||[]}/>
    </section>

    <section className="role-table-card">
      <div className="role-tabs">
        <button className={tab==="recent"?"active":""} onClick={()=>setTab("recent")}>Recent Invoices</button>
        <button className={tab==="pending"?"active":""} onClick={()=>setTab("pending")}>Pending for Approval ({pending})</button>
        <button className={tab==="approved"?"active":""} onClick={()=>setTab("approved")}>Approved ({approved})</button>
        <button className={tab==="rejected"?"active":""} onClick={()=>setTab("rejected")}>Rejected ({rejected})</button>
        <button className={tab==="cancelled"?"active":""} onClick={()=>setTab("cancelled")}>Cancelled ({cancelled})</button>
        <button className={tab==="paid"?"active":""} onClick={()=>setTab("paid")}>Paid ({paid})</button>
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
              <th>Oracle Invoice No</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r:any)=><tr key={r.id}>
              <td>{r.invoiceNumber}</td>
              <td>{r.vendorName}</td>
              <td>{formatDate(r.invoiceDate)}</td>
              <td>{formatMoney(r.invoiceAmount)}</td>
              <td>{(r.poNumbers||[]).join(", ")||"-"}</td>
              <td>{(r.grnNumbers||[]).join(", ")||"-"}</td>
              <td><StatusPill value={r.status}/></td>
              <td>{r.oracleInvoiceNo||"-"}</td>
            </tr>)}
            {!visible.length&&<tr><td colSpan={8} className="empty">No live records available.</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  </div>;
}

function SupplyChainDashboard({ data }: { data: any }) {
  const navigate = useNavigate();
  const [qcRows, setQcRows] = useState<QcPendingGrn[]>([]);
  const [qcLoading, setQcLoading] = useState(true);
  const [qcError, setQcError] = useState("");
  const [selectedQc, setSelectedQc] = useState<QcPendingGrn | null>(null);

  const access = data.accessStatus || {};
  const total = Number(access.total || data.kpis?.totalVendors || 0);
  const enabled = Number(access.portalEnabled || 0);
  const notEnabled = Number(access.notEnabled || Math.max(0, total - enabled));
  const pendingAccess = Number(data.kpis?.pendingVendorAccess || 0);

  const donutStyle = {
    background: `conic-gradient(#16a34a 0 ${total ? (enabled / total) * 100 : 0}%, #d1d5db 0 100%)`
  };

  const onboardingTrend: any[] = data.onboardingTrend || [];
  const maxTrend = Math.max(1, ...onboardingTrend.map((row: any) => Number(row.onboarded || 0)));
  const recentOnboarded: any[] = (data.recentlyOnboardedVendors || []).slice(0, 5);
  const pendingVendors: any[] = (data.pendingVendorRequests || []).slice(0, 5);

  useEffect(() => {
    let active = true;

    setQcLoading(true);
    setQcError("");

    getQcPendingGrns()
      .then((rows) => {
        if (active) {
          setQcRows(Array.isArray(rows) ? rows : []);
        }
      })
      .catch((error: any) => {
        if (active) {
          setQcRows([]);
          setQcError(
            error?.response?.data?.message ||
            error?.message ||
            "Unable to load QC pending GRNs."
          );
        }
      })
      .finally(() => {
        if (active) setQcLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setSelectedQc(null);
    };

    if (selectedQc) window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [selectedQc]);

  const latestQcRows = useMemo(() => {
    return [...qcRows]
      .sort((a, b) => {
        const aDate = a.grnDate ? new Date(a.grnDate).getTime() : 0;
        const bDate = b.grnDate ? new Date(b.grnDate).getTime() : 0;
        return bDate - aDate;
      })
      .slice(0, 5);
  }, [qcRows]);

  return <div className="role-dashboard supply-dashboard">
    <section className="role-kpis" style={{ gridTemplateColumns: "repeat(4, minmax(0, 1fr))" }}>
      <Kpi icon="user" label="Total Vendors" value={data.kpis?.totalVendors} tone="green"/>
      <Kpi icon="admin" label="Active Portal Users" value={data.kpis?.activePortalUsers} tone="blue"/>
      <Kpi icon="history" label="Pending Vendor Access" value={pendingAccess} tone="purple"/>
      <Kpi icon="grn" label="QC Pending GRNs" value={qcRows.length} tone="orange"/>
    </section>

    <section className="role-analytics-grid" style={{ gridTemplateColumns: "repeat(2, minmax(0, 1fr))" }}>
      <div className="role-panel">
        <h3>Vendor Access Status</h3>
        <div className="role-donut-wrap">
          <div className="role-donut" style={donutStyle}>
            <div><strong>{total.toLocaleString()}</strong><span>Total Vendors</span></div>
          </div>
          <ul>
            <li><i className="g"/>Portal Enabled <b>{enabled.toLocaleString()} ({pct(enabled,total)})</b></li>
            <li><i className="gray"/>Not Enabled <b>{notEnabled.toLocaleString()} ({pct(notEnabled,total)})</b></li>
          </ul>
        </div>
      </div>

      <div className="role-panel">
        <h3>Vendor Onboarding Trend (Last 6 Months)</h3>
        <div className="role-trend-chart">
          <div className="role-chart-legend"><span><i className="dark" />Onboarded Vendors</span></div>
          <div className="role-bars">
            {onboardingTrend.map((row: any) => (
              <div className="role-bar-group" key={row.month}>
                <div className="role-bar-pair">
                  <b
                    className="dark"
                    style={{
                      height: `${Math.max(4, (Number(row.onboarded || 0) / maxTrend) * 145)}px`
                    }}
                  />
                </div>
                <span>{row.month}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>

    <section className="role-analytics-grid" style={{ gridTemplateColumns: "repeat(2, minmax(0, 1fr))" }}>
      <div className="role-table-card">
        <div className="role-tabs">
          <button className="active" type="button">Recently Onboarded Vendors</button>
        </div>
        <div className="role-table-scroll">
          <table className="role-table">
            <thead>
              <tr>
                <th>Vendor</th>
                <th>Oracle Vendor ID</th>
                <th>Onboarded Date</th>
              </tr>
            </thead>
            <tbody>
              {recentOnboarded.map((row: any, index: number) => (
                <tr key={row.vendorId || `${row.vendorName}-${index}`}>
                  <td>{row.vendorName}</td>
                  <td>{row.oracleVendorId || "-"}</td>
                  <td>{formatDate(row.onboardedAt)}</td>
                </tr>
              ))}
              {!recentOnboarded.length && <tr><td colSpan={3} className="empty">No onboarded vendors available.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>

      <div className="role-table-card">
        <div className="role-tabs">
          <button className="active" type="button">Pending Vendor Access Requests</button>
        </div>
        <div className="role-table-scroll">
          <table className="role-table">
            <thead>
              <tr>
                <th>Vendor</th>
                <th>Oracle Vendor ID</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {pendingVendors.map((row: any, index: number) => (
                <tr key={row.vendorId || `${row.vendorName}-${index}`}>
                  <td>{row.vendorName}</td>
                  <td>{row.oracleVendorId || "-"}</td>
                  <td><StatusPill value="Not Enabled"/></td>
                </tr>
              ))}
              {!pendingVendors.length && <tr><td colSpan={3} className="empty">No pending vendor access requests.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section className="role-table-card">
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 16,
          padding: "14px 16px 10px"
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 11 }}>
          <span
            style={{
              width: 36,
              height: 36,
              borderRadius: 9,
              display: "grid",
              placeItems: "center",
              background: "#fff5e8",
              color: "#f39a14",
              flex: "0 0 36px"
            }}
          >
            <Icon name="grn" size={22} />
          </span>
          <div>
            <h3 style={{ margin: 0, fontSize: 14, color: "#15213b" }}>QC Pending GRNs</h3>
            <p style={{ margin: "3px 0 0", fontSize: 11, color: "#69778a" }}>
              Latest GRNs pending Quality Control for active, onboarded vendors.
            </p>
          </div>
        </div>

        <button
          type="button"
          onClick={() => navigate("/supply-chain/qc-pending-grns")}
          style={{
            border: 0,
            background: "transparent",
            color: "#0b67d1",
            fontSize: 11,
            fontWeight: 700,
            cursor: "pointer",
            whiteSpace: "nowrap",
            padding: "8px 4px"
          }}
        >
          ›&nbsp;&nbsp;View All
        </button>
      </div>

      <div className="role-table-scroll">
        <table className="role-table">
          <thead>
            <tr>
              <th>Vendor</th>
              <th>PO Number</th>
              <th>PR Number</th>
              <th>Item Description</th>
              <th>QC Status</th>
              <th style={{ textAlign: "center" }}>Action</th>
            </tr>
          </thead>
          <tbody>
            {latestQcRows.map((row, index) => (
              <tr
                key={`${row.grnNumber}-${row.poNumber}-${row.poLineNumber || index}`}
                onDoubleClick={() => setSelectedQc(row)}
                style={{ cursor: "pointer" }}
                title="Double-click to view details"
              >
                <td style={{ fontWeight: 500 }}>{row.vendorName || "-"}</td>
                <td>{row.poNumber || "-"}</td>
                <td>{row.prNumber || "-"}</td>
                <td
                  style={{
                    maxWidth: 420,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap"
                  }}
                  title={row.itemDescription || ""}
                >
                  {row.itemDescription || "-"}
                </td>
                <td><span className="role-status orange">Pending with QC</span></td>
                <td style={{ textAlign: "center" }}>
                  <button
                    type="button"
                    className="table-btn"
                    onClick={() => setSelectedQc(row)}
                  >
                    View Details
                  </button>
                </td>
              </tr>
            ))}

            {qcLoading && (
              <tr><td colSpan={6} className="empty">Loading QC pending GRNs...</td></tr>
            )}

            {!qcLoading && qcError && (
              <tr><td colSpan={6} className="empty">{qcError}</td></tr>
            )}

            {!qcLoading && !qcError && !latestQcRows.length && (
              <tr><td colSpan={6} className="empty">No QC pending GRNs available.</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </section>

    {selectedQc && (
      <div
        role="presentation"
        onMouseDown={(event) => {
          if (event.target === event.currentTarget) setSelectedQc(null);
        }}
        style={{
          position: "fixed",
          inset: 0,
          zIndex: 1000,
          background: "rgba(15, 23, 42, 0.38)",
          display: "grid",
          placeItems: "center",
          padding: 20
        }}
      >
        <div
          role="dialog"
          aria-modal="true"
          aria-label="QC pending GRN details"
          style={{
            width: "min(760px, 96vw)",
            maxHeight: "88vh",
            overflow: "auto",
            background: "#fff",
            borderRadius: 12,
            boxShadow: "0 24px 70px rgba(15, 23, 42, 0.20)",
            border: "1px solid #e4e9e6"
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              gap: 12,
              padding: "16px 18px",
              borderBottom: "1px solid #e8ecea"
            }}
          >
            <div>
              <h3 style={{ margin: 0, fontSize: 16, color: "#15213b" }}>QC Pending GRN Details</h3>
              <p style={{ margin: "4px 0 0", fontSize: 11, color: "#69778a" }}>
                {selectedQc.vendorName || "Vendor"} · GRN {selectedQc.grnNumber || "-"}
              </p>
            </div>
            <button
              type="button"
              onClick={() => setSelectedQc(null)}
              aria-label="Close details"
              style={{
                border: "1px solid #dfe6e2",
                background: "#fff",
                width: 32,
                height: 32,
                borderRadius: 7,
                cursor: "pointer",
                fontSize: 20,
                lineHeight: 1
              }}
            >
              ×
            </button>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
              gap: 12,
              padding: 18
            }}
          >
            {[
              ["Vendor", selectedQc.vendorName],
              ["Vendor Code", selectedQc.vendorCode],
              ["Oracle Vendor ID", selectedQc.oracleVendorId],
              ["PO Number", selectedQc.poNumber],
              ["PO Date", formatDate(selectedQc.poDate)],
              ["PR Number", selectedQc.prNumber || "-"],
              ["GRN Number", selectedQc.grnNumber],
              ["GRN Date", formatDate(selectedQc.grnDate)],
              ["PO Line", selectedQc.poLineNumber || "-"],
              ["Item Code", selectedQc.itemCode || "-"],
              ["UOM", selectedQc.uom || "-"],
              ["Received Quantity", selectedQc.receivedQuantity === null || selectedQc.receivedQuantity === undefined ? "-" : Number(selectedQc.receivedQuantity).toLocaleString()],
              ["QC Status", selectedQc.qcStatus || "Pending with QC"],
              ["Oracle QC Status", selectedQc.oracleQcStatus || "-"],
              ["Aging", `${Number(selectedQc.agingDays || 0)} day${Number(selectedQc.agingDays || 0) === 1 ? "" : "s"}`],
            ].map(([label, value]) => (
              <div
                key={String(label)}
                style={{
                  border: "1px solid #e7ece9",
                  borderRadius: 8,
                  padding: "11px 12px",
                  minWidth: 0
                }}
              >
                <small style={{ display: "block", color: "#6b7788", marginBottom: 5 }}>{label}</small>
                <strong style={{ display: "block", color: "#24324a", fontSize: 12, overflowWrap: "anywhere" }}>
                  {String(value || "-")}
                </strong>
              </div>
            ))}

            <div
              style={{
                gridColumn: "1 / -1",
                border: "1px solid #e7ece9",
                borderRadius: 8,
                padding: "11px 12px"
              }}
            >
              <small style={{ display: "block", color: "#6b7788", marginBottom: 5 }}>Item Description</small>
              <strong style={{ display: "block", color: "#24324a", fontSize: 12, lineHeight: 1.5 }}>
                {selectedQc.itemDescription || "-"}
              </strong>
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: 10,
              padding: "0 18px 18px"
            }}
          >
            <button type="button" className="secondary-btn" onClick={() => setSelectedQc(null)}>
              Close
            </button>
            <button
              type="button"
              className="primary-btn"
              onClick={() => navigate("/supply-chain/qc-pending-grns")}
            >
              Open QC Pending GRNs
            </button>
          </div>
        </div>
      </div>
    )}
  </div>;
}

export function InternalDashboard() {
  const { user } = useAuth(); const [data,setData]=useState<DashboardData|null>(null); const [error,setError]=useState("");
  useEffect(()=>{getLiveDashboard().then(setData).catch((e:any)=>setError(e?.response?.data?.message||e?.message||"Unable to load live dashboard data."));},[]);
  if(error)return <div className="page-card"><p className="error-message">{error}</p></div>;
  if(!data)return <div className="page-card"><p>Loading live dashboard data...</p></div>;
  const roles=(user?.roles??[]).map(r=>r.toUpperCase()),permissions=user?.permissions??[]; const isFinance=roles.includes("FINANCE")||permissions.includes("INVOICE.VIEW_ALL");
  return isFinance?<FinanceDashboard data={data.finance}/>:<SupplyChainDashboard data={data.supplyChain}/>;
}
