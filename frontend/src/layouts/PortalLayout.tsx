import { useEffect, useState } from "react";
import {
  NavLink,
  Outlet,
  useLocation,
  useNavigate,
} from "react-router-dom";

import { useAuth } from "../auth/AuthContext";
import { api } from "../api/client";
import { Icon } from "../components/Icons";

type MenuItem = [string, string, string];

export function PortalLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [count, setCount] = useState(0);

  useEffect(() => {
    if (!user) return;

    let mounted = true;

    const loadUnreadCount = () => {
      api
        .get("/notifications/unread-count")
        .then((response) => {
          if (mounted) {
            setCount(response.data.count || 0);
          }
        })
        .catch(() => {
          // Notification count failure should not block portal UI.
        });
    };

    loadUnreadCount();

    const timer = setInterval(loadUnreadCount, 30000);

    return () => {
      mounted = false;
      clearInterval(timer);
    };
  }, [user]);

  if (!user) {
    return null;
  }

  const isVendor = user.userType === "VENDOR";
  const isAdmin = user.userType === "ADMIN";

  const items: MenuItem[] = isVendor
    ? [
        ["/vendor", "Dashboard", "dashboard"],
        ["/purchase-orders", "Purchase Orders", "po"],
        ["/grns", "GRNs", "grn"],
        ["/invoices/new", "Create Invoice", "invoice"],
        ["/invoices", "Invoice History", "history"],
        ["/payments", "Payments", "payment"],
        ["/vendor-profile", "Vendor Profile", "user"],
        ["/downloads", "Downloads", "download"],
        ["/support", "Support", "support"],
      ]
    : isAdmin
      ? [
          ["/admin", "Dashboard", "dashboard"],
          ["/admin/users", "User Management", "user"],
          ["/admin/roles", "Roles & Permissions", "admin"],
          ["/admin/vendors", "Vendor Access", "po"],
          ["/integration", "Integration Support", "integration"],
          ["/admin/audit", "Audit Logs", "history"],
          ["/admin/system", "System Configuration", "support"],
        ]
      : [
          ["/internal", "Dashboard", "dashboard"],
          ["/supply-chain/vendors", "Vendor Access", "admin"],
          ["/integration", "Integration Support", "integration"],
        ];

  const isMenuItemActive = (path: string) => {
    return location.pathname === path;
  };

  return (
    <div className="portal-shell">
      <aside className="portal-sidebar">
        <div className="pc-brand">
          <img
            src="/pakistan-cables-logo.png"
            alt="Pakistan Cables"
          />

          <div>
            <strong>Pakistan Cables</strong>
            <span>Business Partner&apos;s Portal</span>
          </div>
        </div>

        <nav>
          {items.map(([to, label, icon]) => (
            <NavLink
              key={to}
              to={to}
              end
              className={() =>
                isMenuItemActive(to)
                  ? "side-link active"
                  : "side-link"
              }
            >
              <Icon name={icon} />
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>

        <button
          type="button"
          className="side-link side-logout"
          onClick={() => {
            logout();
            navigate("/login");
          }}
        >
          <Icon name="logout" />
          <span>Logout</span>
        </button>
      </aside>

      <section className="portal-main">
        <header className="portal-topbar">
          <div>
            <h1>
              {isVendor
                ? "Dashboard"
                : isAdmin
                  ? "Administration"
                  : "Internal Portal"}
            </h1>

            <p>Welcome back, {user.fullName}.</p>
          </div>

          <div className="top-actions">
            <span className="role-pill">
              {user.userType}
            </span>

            <button
              type="button"
              className="bell-btn"
              onClick={() => navigate("/notifications")}
              aria-label="Notifications"
            >
              <Icon name="bell" />

              {count > 0 && (
                <b>{count > 99 ? "99+" : count}</b>
              )}
            </button>

            <div className="user-chip">
              <span>
                {user.fullName.charAt(0)}
              </span>

              <strong>{user.fullName}</strong>
            </div>
          </div>
        </header>

        <Outlet />
      </section>
    </div>
  );
}