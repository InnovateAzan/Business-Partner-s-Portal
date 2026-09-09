import { useEffect, useMemo, useState } from "react";
import { Icon } from "../components/Icons";
import { getLiveDashboard } from "../api/portal";

function formatDate(value?: string | null) {
  if (!value) return "-";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}
function timeAgo(value?: string | null) {
  if (!value) return "-";
  const d = new Date(value); if (Number.isNaN(d.getTime())) return "-";
  const sec = Math.max(0, Math.floor((Date.now() - d.getTime()) / 1000));
  if (sec < 60) return `${sec}s ago`; if (sec < 3600) return `${Math.floor(sec/60)} min ago`; if (sec < 86400) return `${Math.floor(sec/3600)} hours ago`; return `${Math.floor(sec/86400)} days ago`;
}
function Status({ value }: { value: string }) { return <span className={`role-status ${value === "Inactive" || value === "Disabled" ? "red" : "green"}`}>{value}</span>; }
function Kpi({ icon,label,value,tone }: { icon:string; label:string; value:number; tone:string }) { return <div className="role-kpi"><span className={`role-kpi-icon ${tone}`}><Icon name={icon} size={24}/></span><div><small>{label}</small><strong>{Number(value||0).toLocaleString()}</strong><p className="up">Live <em>current data</em></p></div></div>; }

export function AdminDashboard() {
  const [payload,setPayload]=useState<any>(null); const [error,setError]=useState("");
  useEffect(()=>{getLiveDashboard().then(setPayload).catch((e:any)=>setError(e?.response?.data?.message||e?.message||"Unable to load live dashboard data."));},[]);
  const data=payload?.admin;
  const roleTotal=useMemo(()=>Number(data?.roleDistribution?.reduce((s:number,r:any)=>s+Number(r.count||0),0)||0),[data]);
  const roleColors=["#16a34a","#4b9cf2","#f6a512","#7c52e5","#94a3b8","#14b8a6"];
  const donut=useMemo(()=>{let cursor=0; const parts=(data?.roleDistribution||[]).map((r:any,i:number)=>{const start=cursor; cursor += roleTotal?Number(r.count||0)/roleTotal*100:0; return `${roleColors[i%roleColors.length]} ${start}% ${cursor}%`;}); return {background:parts.length?`conic-gradient(${parts.join(",")})`:"#e5e7eb"};},[data,roleTotal]);
  if(error)return <div className="page-card"><p className="error-message">{error}</p></div>;
  if(!data)return <div className="page-card"><p>Loading live dashboard data...</p></div>;
  const trend=data.userTrend||[]; const trendMax=Math.max(1,...trend.flatMap((r:any)=>[Number(r.newUsers||0),Number(r.activeUsers||0)]));
  return <div className="role-dashboard admin-role-dashboard">
    <section className="role-kpis"><Kpi icon="user" label="Total Users" value={data.kpis?.totalUsers} tone="green"/><Kpi icon="admin" label="User Roles" value={data.kpis?.userRoles} tone="blue"/><Kpi icon="po" label="Total Vendors" value={data.kpis?.totalVendors} tone="orange"/><Kpi icon="invoice" label="Total Invoices" value={data.kpis?.totalInvoices} tone="purple"/><Kpi icon="support" label="Integration Errors" value={data.kpis?.integrationErrors} tone="red"/></section>
    <section className="admin-analytics-grid">
      <div className="role-panel"><h3>User Distribution (By Role)</h3><div className="role-donut-wrap"><div className="role-donut" style={donut}><div><strong>{Number(data.kpis?.totalUsers||0).toLocaleString()}</strong><span>Users</span></div></div><ul>{(data.roleDistribution||[]).slice(0,6).map((r:any,i:number)=><li key={r.code}><i style={{background:roleColors[i%roleColors.length]}}/> {r.name} <b>{Number(r.count||0).toLocaleString()} ({roleTotal?((Number(r.count||0)/roleTotal)*100).toFixed(1):"0.0"}%)</b></li>)}</ul></div></div>
      <div className="role-panel"><h3>User Trend (Last 6 Months)</h3><div className="role-trend-chart"><div className="role-chart-legend"><span><i className="light"/>New Users</span><span><i className="dark"/>Active Users</span></div><div className="role-bars">{trend.map((r:any)=><div className="role-bar-group" key={r.month}><div className="role-bar-pair"><b style={{height:`${Math.max(4,Number(r.newUsers||0)/trendMax*145)}px`}}/><b className="dark" style={{height:`${Math.max(4,Number(r.activeUsers||0)/trendMax*145)}px`}}/></div><span>{r.month}</span></div>)}</div></div></div>
      <div className="role-panel admin-activity"><div className="role-panel-head"><h3>Recent Activities</h3><span>Live</span></div>{(data.recentActivities||[]).slice(0,5).map((a:any)=><div className="activity-row" key={a.id}><span className="activity-icon green"><Icon name={a.entityType?.toLowerCase().includes("user")?"user":a.entityType?.toLowerCase().includes("invoice")?"invoice":"support"} size={17}/></span><div><strong>{String(a.action||"Activity").replaceAll("_"," ")}</strong><small>{a.userName||a.userEmail||`${a.entityType||"Record"}${a.entityId?` · ${a.entityId}`:""}`}</small></div><em>{timeAgo(a.createdAt)}</em></div>)}{!(data.recentActivities||[]).length&&<p className="empty">No audit activity available.</p>}</div>
    </section>
    <section className="admin-bottom-grid">
      <div className="role-panel compact-table"><div className="role-panel-head"><h3>Recent Users</h3><span>Live</span></div><table className="role-table"><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Created On</th></tr></thead><tbody>{(data.recentUsers||[]).slice(0,5).map((r:any)=><tr key={r.id}><td>{r.name}</td><td>{r.email}</td><td>{(r.roles||[]).join(", ")||r.userType||"-"}</td><td><Status value={r.status}/></td><td>{formatDate(r.createdOn)}</td></tr>)}{!(data.recentUsers||[]).length&&<tr><td colSpan={5} className="empty">No live users available.</td></tr>}</tbody></table></div>
      <div className="role-panel compact-table"><div className="role-panel-head"><h3>Recent Vendors</h3><span>Live</span></div><table className="role-table"><thead><tr><th>Vendor</th><th>Oracle Vendor ID</th><th>Portal Users</th><th>Status</th><th>Last Access</th></tr></thead><tbody>{(data.recentVendors||[]).slice(0,5).map((r:any)=><tr key={r.vendorId}><td>{r.vendorName}</td><td>{r.oracleVendorId||"-"}</td><td>{r.portalUsers||0}</td><td><Status value={r.accessStatus}/></td><td>{formatDate(r.lastAccess)}</td></tr>)}{!(data.recentVendors||[]).length&&<tr><td colSpan={5} className="empty">No live vendors available.</td></tr>}</tbody></table></div>
      <div className="role-panel compact-table"><div className="role-panel-head"><h3>Integration Queue</h3><span>Live</span></div><table className="role-table"><thead><tr><th>Process</th><th>Total</th><th>Success</th><th>Failed</th><th>Pending</th></tr></thead><tbody>{(data.integrationQueue||[]).slice(0,6).map((r:any)=><tr key={r.process}><td>{r.process}</td><td>{r.total}</td><td className="text-green">{r.success}</td><td className="text-red">{r.failed}</td><td className="text-orange">{r.pending}</td></tr>)}{!(data.integrationQueue||[]).length&&<tr><td colSpan={5} className="empty">No integration queue records available.</td></tr>}</tbody></table></div>
    </section>
  </div>;
}
