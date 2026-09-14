import { useEffect, useMemo, useState } from "react";
import { Icon } from "../components/Icons";
import { useAuth } from "../auth/AuthContext";
import { getLiveDashboard } from "../api/portal";
import { exportRowsToCsv } from "../utils/exportCsv";

type DashboardData = any;
type TabKey = string;

function formatDate(value?: string | null) {
  if (!value) return "-";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}
function formatMoney(value?: number | null) { return Number(value || 0).toLocaleString(); }
function pct(part: number, total: number) { return total ? `${((part / total) * 100).toFixed(1)}%` : "0.0%"; }
function inRange(value: string | null | undefined, from: string, to: string) {
  if (!from && !to) return true;
  if (!value) return false;
  const d = new Date(value); if (Number.isNaN(d.getTime())) return false;
  if (from && d < new Date(`${from}T00:00:00`)) return false;
  if (to && d > new Date(`${to}T23:59:59`)) return false;
  return true;
}
function StatusPill({ value }: { value: string }) {
  const v = (value || "-").toLowerCase();
  const cls = v.includes("reject") || v.includes("fail") || v.includes("disabled") ? "red" : v.includes("pending") ? "orange" : v.includes("review") || v.includes("partial") || v.includes("not invoiced") ? "blue" : "green";
  return <span className={`role-status ${cls}`}>{value || "-"}</span>;
}
function TrendBars({ rows, leftLabel, rightLabel }: { rows: any[]; leftLabel: string; rightLabel: string }) {
  const leftKey = leftLabel === "Purchase Orders" ? "purchaseOrders" : "submitted";
  const rightKey = rightLabel === "GRNs" ? "grns" : "integrated";
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
  const [tab,setTab]=useState<TabKey>("recent"), [from,setFrom]=useState(""), [to,setTo]=useState(""), [vendor,setVendor]=useState(""), [status,setStatus]=useState(""), [search,setSearch]=useState("");
  const [applied,setApplied]=useState({from:"",to:"",vendor:"",status:"",search:""}); const [page,setPage]=useState(1); const pageSize=10;
  const rows = data.invoices || []; const statusData=data.status||{};
  const vendors=useMemo(()=>Array.from(new Set(rows.map((r:any)=>r.vendorName).filter(Boolean))).sort() as string[],[rows]);
  const tabRows=useMemo(()=>rows.filter((r:any)=>tab==="recent"||tab==="pending"&&r.status==="Pending"||tab==="rejected"&&r.status==="Rejected"||tab==="review"&&r.status==="Under Review"||tab==="paid"&&r.status==="Paid"),[rows,tab]);
  const filtered=useMemo(()=>tabRows.filter((r:any)=>{
    const q=applied.search.toLowerCase(); const text=[r.invoiceNumber,r.vendorName,...(r.poNumbers||[]),...(r.grnNumbers||[]),r.oracleInvoiceNo,r.status].join(" ").toLowerCase();
    return inRange(r.invoiceDate||r.submissionDate,applied.from,applied.to)&&(!applied.vendor||r.vendorName===applied.vendor)&&(!applied.status||r.status===applied.status)&&(!q||text.includes(q));
  }),[tabRows,applied]);
  const pages=Math.max(1,Math.ceil(filtered.length/pageSize)), safePage=Math.min(page,pages), visible=filtered.slice((safePage-1)*pageSize,safePage*pageSize);
  useEffect(()=>setPage(1),[tab,applied]);
  const total=Number(statusData.total||0), integrated=Number(statusData.integrated||0), pending=Number(statusData.pending||0), rejected=Number(statusData.rejected||0), review=Number(statusData.underReview||0);
  const donutStyle={background:`conic-gradient(#16a34a 0 ${total?integrated/total*100:0}%, #fbbf24 0 ${total?(integrated+pending)/total*100:0}%, #ef4444 0 ${total?(integrated+pending+rejected)/total*100:0}%, #7c3aed 0 100%)`};
  const exportRows=()=>exportRowsToCsv("finance-dashboard-invoices.csv",["Invoice No","Vendor","Invoice Date","Amount (PKR)","PO No(s)","GRN No(s)","Status","Oracle Invoice No"],filtered.map((r:any)=>[r.invoiceNumber,r.vendorName,formatDate(r.invoiceDate),r.invoiceAmount,(r.poNumbers||[]).join(", "),(r.grnNumbers||[]).join(", "),r.status,r.oracleInvoiceNo||""]));
  return <div className="role-dashboard finance-dashboard">
    <section className="role-kpis"><Kpi icon="invoice" label="Total Invoices" value={data.kpis?.totalInvoices} tone="green"/><Kpi icon="admin" label="Integrated in Oracle" value={data.kpis?.integratedInOracle} tone="blue"/><Kpi icon="history" label="Pending in Oracle" value={data.kpis?.pendingInOracle} tone="orange"/><Kpi icon="support" label="Rejected in Oracle" value={data.kpis?.rejectedInOracle} tone="red"/><Kpi icon="invoice" label="Under Review" value={data.kpis?.underReview} tone="purple"/></section>
    <section className="role-analytics-grid"><div className="role-panel"><h3>Invoice Status</h3><div className="role-donut-wrap"><div className="role-donut" style={donutStyle}><div><strong>{total.toLocaleString()}</strong><span>Total</span></div></div><ul><li><i className="g"/>Integrated <b>{integrated.toLocaleString()} ({pct(integrated,total)})</b></li><li><i className="o"/>Pending <b>{pending.toLocaleString()} ({pct(pending,total)})</b></li><li><i className="r"/>Rejected <b>{rejected.toLocaleString()} ({pct(rejected,total)})</b></li><li><i className="p"/>Under Review <b>{review.toLocaleString()} ({pct(review,total)})</b></li></ul></div></div><div className="role-panel"><h3>Invoices Trend (Last 6 Months)</h3><TrendBars rows={data.trend||[]} leftLabel="Submitted" rightLabel="Integrated"/></div><VendorBars title="Top Vendors (by Invoice Amount)" rows={data.topVendors||[]}/></section>
    <section className="role-table-card"><div className="role-tabs"><button className={tab==="recent"?"active":""} onClick={()=>setTab("recent")}>Recent Invoices</button><button className={tab==="pending"?"active":""} onClick={()=>setTab("pending")}>Pending in Oracle ({pending})</button><button className={tab==="rejected"?"active":""} onClick={()=>setTab("rejected")}>Rejected ({rejected})</button><button className={tab==="review"?"active":""} onClick={()=>setTab("review")}>Under Review ({review})</button><button className={tab==="paid"?"active":""} onClick={()=>setTab("paid")}>Recently Paid ({data.kpis?.recentlyPaid||0})</button><button className="role-export" onClick={exportRows}>↧ &nbsp; Export to Excel</button></div>
      <div className="role-filters"><label><span>From Date</span><input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label><label><span>To Date</span><input type="date" value={to} onChange={e=>setTo(e.target.value)}/></label><label><span>Vendor</span><select value={vendor} onChange={e=>setVendor(e.target.value)}><option value="">All Vendors</option>{vendors.map(v=><option key={v}>{v}</option>)}</select></label><label><span>Status</span><select value={status} onChange={e=>setStatus(e.target.value)}><option value="">All Statuses</option>{["Integrated","Pending","Rejected","Under Review","Paid"].map(s=><option key={s}>{s}</option>)}</select></label><label className="role-search-box"><span>&nbsp;</span><input value={search} onChange={e=>setSearch(e.target.value)} placeholder="Search invoice no, PO no, GRN no..."/></label><button className="role-search" onClick={()=>setApplied({from,to,vendor,status,search})}>⌕ &nbsp; Search</button><button className="role-clear" onClick={()=>{setFrom("");setTo("");setVendor("");setStatus("");setSearch("");setApplied({from:"",to:"",vendor:"",status:"",search:""});}}>Clear</button></div>
      <div className="role-table-scroll"><table className="role-table"><thead><tr><th>Invoice No</th><th>Vendor</th><th>Invoice Date</th><th>Amount (PKR)</th><th>PO No(s)</th><th>GRN No(s)</th><th>Status</th><th>Oracle Invoice No</th><th>Actions</th></tr></thead><tbody>{visible.map((r:any)=><tr key={r.id}><td>{r.invoiceNumber}</td><td>{r.vendorName}</td><td>{formatDate(r.invoiceDate)}</td><td>{formatMoney(r.invoiceAmount)}</td><td>{(r.poNumbers||[]).join(", ")||"-"}</td><td>{(r.grnNumbers||[]).join(", ")||"-"}</td><td><StatusPill value={r.status}/></td><td>{r.oracleInvoiceNo||"-"}</td><td><span className="role-actions">⌾ &nbsp; ▱</span></td></tr>)}{!visible.length&&<tr><td colSpan={9} className="empty">No live records match the selected filters.</td></tr>}</tbody></table></div><Pager page={safePage} pages={pages} total={filtered.length} pageSize={pageSize} onPage={setPage}/>
    </section></div>;
}

function SupplyChainDashboard({ data }: { data: any }) {
  const [tab, setTab] = useState<TabKey>("po");
  const [page, setPage] = useState(1);
  const pageSize = 10;

  const source: any[] =
    tab === "po"
      ? (data.purchaseOrders || [])
      : tab === "grn"
        ? (data.grns || [])
        : tab === "pending"
          ? (data.pendingVendorRequests || [])
          : (data.recentlyOnboardedVendors || []);

  const pages = Math.max(1, Math.ceil(source.length / pageSize));
  const safePage = Math.min(page, pages);
  const visible = source.slice((safePage - 1) * pageSize, safePage * pageSize);

  useEffect(() => setPage(1), [tab]);

  const a = data.accessStatus || {};
  const total = Number(a.total || 0);
  const enabled = Number(a.portalEnabled || 0);
  const notEnabled = Number(a.notEnabled || 0);
  const donutStyle = {
    background: `conic-gradient(#16a34a 0 ${total ? enabled / total * 100 : 0}%, #d1d5db 0 100%)`
  };

  return <div className="role-dashboard supply-dashboard">
    <section className="role-kpis">
      <Kpi icon="user" label="Total Vendors" value={data.kpis?.totalVendors} tone="purple"/>
      <Kpi icon="invoice" label="Active Portal Users" value={data.kpis?.activePortalUsers} tone="blue"/>
      <Kpi icon="po" label="Purchase Orders" value={data.kpis?.purchaseOrders} tone="orange"/>
      <Kpi icon="grn" label="GRNs Received" value={data.kpis?.grnsReceived} tone="green"/>
      <Kpi icon="support" label="Open Vendor Requests" value={data.kpis?.openVendorRequests} tone="red"/>
    </section>

    <section className="role-analytics-grid">
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
        <h3>PO &amp; GRN Trend (Last 6 Months)</h3>
        <TrendBars rows={data.trend||[]} leftLabel="Purchase Orders" rightLabel="GRNs"/>
      </div>
      <VendorBars title="Top Vendors (by PO Amount)" rows={data.topVendors||[]}/>
    </section>

    <section className="role-table-card">
      <div className="role-tabs">
        <button className={tab==="po"?"active":""} onClick={()=>setTab("po")}>Recent Purchase Orders</button>
        <button className={tab==="grn"?"active":""} onClick={()=>setTab("grn")}>Recent GRNs</button>
        <button className={tab==="pending"?"active":""} onClick={()=>setTab("pending")}>Pending Vendor Requests</button>
        <button className={tab==="onboarded"?"active":""} onClick={()=>setTab("onboarded")}>Recently Onboarded Vendors</button>
      </div>

      <div className="role-table-scroll">
        {tab === "po" ? (
          <table className="role-table">
            <thead><tr><th>PO Number</th><th>Vendor</th><th>PO Date</th><th>Amount (PKR)</th><th>Status</th><th>GRN Status</th><th>Invoice Status</th></tr></thead>
            <tbody>
              {visible.map((r:any)=><tr key={r.id}><td>{r.poNumber}</td><td>{r.vendorName}</td><td>{formatDate(r.poDate)}</td><td>{formatMoney(r.amount)}</td><td><StatusPill value={r.status}/></td><td><StatusPill value={r.grnStatus}/></td><td><StatusPill value={r.invoiceStatus}/></td></tr>)}
              {!visible.length&&<tr><td colSpan={7} className="empty">No live records available.</td></tr>}
            </tbody>
          </table>
        ) : tab === "grn" ? (
          <table className="role-table">
            <thead><tr><th>GRN Number</th><th>Vendor</th><th>PO Number</th><th>GRN Date</th><th>Status</th><th>QC Status</th></tr></thead>
            <tbody>
              {visible.map((r:any)=><tr key={r.id}><td>{r.grnNumber}</td><td>{r.vendorName}</td><td>{r.poNumber}</td><td>{formatDate(r.grnDate)}</td><td><StatusPill value={r.status}/></td><td>{r.qcStatus||"-"}</td></tr>)}
              {!visible.length&&<tr><td colSpan={6} className="empty">No live records available.</td></tr>}
            </tbody>
          </table>
        ) : (
          <table className="role-table">
            <thead><tr><th>Vendor</th><th>Oracle Vendor ID</th><th>Status</th><th>Portal Users</th><th>Last Access / Onboarded</th></tr></thead>
            <tbody>
              {visible.map((r:any,i:number)=><tr key={r.vendorId||`${r.vendorName}-${i}`}><td>{r.vendorName}</td><td>{r.oracleVendorId||"-"}</td><td><StatusPill value={r.accessStatus||(r.active?"Active":"Inactive")}/></td><td>{r.portalUsers??r.activePortalUsers??1}</td><td>{formatDate(r.lastAccess||r.onboardedAt)}</td></tr>)}
              {!visible.length&&<tr><td colSpan={5} className="empty">No live records available.</td></tr>}
            </tbody>
          </table>
        )}
      </div>

      <Pager page={safePage} pages={pages} total={source.length} pageSize={pageSize} onPage={setPage}/>
      <div className="role-info">ⓘ &nbsp; You are logged in as <strong>Supply Chain</strong>. Dashboard values are loaded from live portal data.</div>
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
