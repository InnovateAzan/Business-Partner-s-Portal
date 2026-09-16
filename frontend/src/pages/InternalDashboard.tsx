import { useEffect, useMemo, useState } from "react";
import { Icon } from "../components/Icons";
import { useAuth } from "../auth/AuthContext";
import { getLiveDashboard } from "../api/portal";

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
        <h3>Invoices Trend (Last 6 Months)</h3>
        <TrendBars rows={data.trend||[]} leftLabel="Submitted" rightLabel="Approved"/>
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
  const access = data.accessStatus || {};
  const total = Number(access.total || data.kpis?.totalVendors || 0);
  const enabled = Number(access.portalEnabled || 0);
  const notEnabled = Number(access.notEnabled || Math.max(0, total - enabled));
  const pendingAccess = Number(data.kpis?.pendingVendorAccess || 0);
  const onboardedThisFiscalYear = Number(data.kpis?.onboardedThisFiscalYear || 0);

  const donutStyle = {
    background: `conic-gradient(#16a34a 0 ${total ? (enabled / total) * 100 : 0}%, #d1d5db 0 100%)`
  };

  const onboardingTrend: any[] = data.onboardingTrend || [];
  const maxTrend = Math.max(1, ...onboardingTrend.map((row: any) => Number(row.onboarded || 0)));
  const recentOnboarded: any[] = (data.recentlyOnboardedVendors || []).slice(0, 5);
  const pendingVendors: any[] = (data.pendingVendorRequests || []).slice(0, 5);

  return <div className="role-dashboard supply-dashboard">
    <section className="role-kpis" style={{ gridTemplateColumns: "repeat(4, minmax(0, 1fr))" }}>
      <Kpi icon="user" label="Total Vendors" value={data.kpis?.totalVendors} tone="green"/>
      <Kpi icon="admin" label="Active Portal Users" value={data.kpis?.activePortalUsers} tone="blue"/>
      <Kpi icon="history" label="Pending Vendor Access" value={pendingAccess} tone="purple"/>
      <Kpi icon="user" label="Onboarded Vendors (This FY)" value={onboardedThisFiscalYear} tone="orange"/>
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
