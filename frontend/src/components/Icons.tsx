import type { ReactNode } from "react";
const paths:Record<string,ReactNode>={
 dashboard:<><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></>,
 po:<><path d="M6 2h9l4 4v16H6z"/><path d="M14 2v5h5M9 12h7M9 16h7"/></>,
 grn:<><rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/></>,
 invoice:<><path d="M6 2h9l4 4v16H6z"/><path d="M14 2v5h5M9 12h6M9 16h4"/></>,
 payment:<><rect x="3" y="6" width="18" height="13" rx="2"/><path d="M3 10h18M7 15h3"/></>,
 user:<><circle cx="12" cy="8" r="4"/><path d="M4 21c1-5 4-7 8-7s7 2 8 7"/></>,
 bell:<><path d="M18 8a6 6 0 10-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9"/><path d="M10 21h4"/></>,
 support:<><circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.7 2.7 0 015.2 1c0 2-2.7 2.2-2.7 4M12 18h.01"/></>,
 download:<><path d="M12 3v12M7 10l5 5 5-5M5 21h14"/></>,
 logout:<><path d="M10 17l5-5-5-5M15 12H3M14 3h7v18h-7"/></>,
 admin:<><path d="M12 2l8 4v6c0 5-3.3 8.3-8 10-4.7-1.7-8-5-8-10V6z"/><path d="M9 12l2 2 4-5"/></>,
 integration:<><path d="M7 7h10v10H7zM2 12h5M17 12h5M12 2v5M12 17v5"/></>,
 history:<><path d="M3 12a9 9 0 109-9 9 9 0 00-6.4 2.6L3 8"/><path d="M3 3v5h5M12 7v6l4 2"/></>,
 signup:<><circle cx="10" cy="8" r="4"/><path d="M3 21c1-5 4-7 7-7 2 0 4 .7 5.5 2M18 11v6M15 14h6"/></>
};
export function Icon({name,size=20}:{name:string;size?:number}){return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">{paths[name]??paths.dashboard}</svg>}
