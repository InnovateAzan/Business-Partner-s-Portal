import {
  useEffect,
  useRef,
  useState,
} from "react";

import {
  NavLink,
  Outlet,
  useNavigate,
} from "react-router-dom";

import { useAuth } from "../auth/AuthContext";
import { api } from "../api/client";
import { getMySupplier } from "../api/portal";
import { Icon } from "../components/Icons";
import type { OracleSupplier } from "../types";

type MenuItem = [
  string,
  string,
  string,
];

type NotificationItem = {
  id: string;
  title: string;
  message: string;
  notificationType?: string;
  entityType?: string;
  entityId?: string;
  isRead: boolean;
  createdAt: string;
};

export function PortalLayout() {
  const { user, logout } = useAuth();

  const navigate =
    useNavigate();

  const [
    unreadCount,
    setUnreadCount,
  ] = useState(0);

  const [
    notifications,
    setNotifications,
  ] = useState<
    NotificationItem[]
  >([]);

  const [
    notificationOpen,
    setNotificationOpen,
  ] = useState(false);

  const [
    notificationLoading,
    setNotificationLoading,
  ] = useState(false);

  const notificationRef =
    useRef<HTMLDivElement | null>(
      null
    );

  const profileRef =
    useRef<HTMLDivElement | null>(
      null
    );

  const [profileOpen, setProfileOpen] = useState(false);
  const [profileLoading, setProfileLoading] = useState(false);
  const [supplier, setSupplier] = useState<OracleSupplier | null>(null);

  const [
    sidebarPinnedOpen,
    setSidebarPinnedOpen,
  ] = useState(() =>
    window.localStorage.getItem(
      "portal-sidebar-pinned"
    ) === "true"
  );

  const [
    sidebarHovered,
    setSidebarHovered,
  ] = useState(false);

  const sidebarExpanded =
    sidebarPinnedOpen ||
    sidebarHovered;

  useEffect(() => {
    window.localStorage.setItem(
      "portal-sidebar-pinned",
      sidebarPinnedOpen
        ? "true"
        : "false"
    );
  }, [sidebarPinnedOpen]);

  // ============================================================
  // UNREAD COUNT
  // ============================================================

  useEffect(() => {
    if (!user) {
      return;
    }

    let mounted = true;

    const loadUnreadCount =
      async () => {
        try {
          const response =
            await api.get(
              "/notifications/unread-count"
            );

          if (mounted) {
            setUnreadCount(
              response.data?.count ??
                0
            );
          }
        } catch {
          // Notification failure
          // should not block UI.
        }
      };

    loadUnreadCount();

    const timer =
      window.setInterval(
        loadUnreadCount,
        30000
      );

    return () => {
      mounted = false;

      window.clearInterval(
        timer
      );
    };
  }, [user]);

  // ============================================================
  // CLOSE NOTIFICATION / PROFILE POPUP
  // ============================================================

  useEffect(() => {
    const handleOutsideClick = (
      event: MouseEvent
    ) => {
      if (
        notificationRef.current &&
        !notificationRef.current.contains(
          event.target as Node
        )
      ) {
        setNotificationOpen(false);
      }

      if (
        profileRef.current &&
        !profileRef.current.contains(
          event.target as Node
        )
      ) {
        setProfileOpen(false);
      }
    };

    const handleEscape = (
      event: KeyboardEvent
    ) => {
      if (
        event.key === "Escape"
      ) {
        setNotificationOpen(false);
        setProfileOpen(false);
      }
    };

    document.addEventListener(
      "mousedown",
      handleOutsideClick
    );

    document.addEventListener(
      "keydown",
      handleEscape
    );

    return () => {
      document.removeEventListener(
        "mousedown",
        handleOutsideClick
      );

      document.removeEventListener(
        "keydown",
        handleEscape
      );
    };
  }, []);

  if (!user) {
    return null;
  }

  const isVendor =
    user.userType === "VENDOR";

  const isAdmin =
    user.userType === "ADMIN";

  const internalRoles =
    (user.roles ?? []).map((role) => role.toUpperCase());

  const isFinance =
    !isVendor &&
    !isAdmin &&
    (internalRoles.includes("FINANCE") ||
      user.permissions.includes("INVOICE.VIEW_ALL"));

  const isSupplyChain =
    !isVendor &&
    !isAdmin &&
    !isFinance &&
    (internalRoles.includes("SUPPLY_CHAIN") ||
      user.permissions.includes("VENDOR.MANAGE"));

  // ============================================================
  // SIDEBAR
  // ============================================================

  const items: MenuItem[] =
    isVendor
      ? [
          [
            "/vendor",
            "Dashboard",
            "dashboard",
          ],

          [
            "/purchase-orders",
            "Purchase Orders",
            "po",
          ],

          [
            "/invoices/new",
            "Submit Invoice",
            "invoice",
          ],

          [
            "/invoices",
            "Invoice History",
            "history",
          ],

          [
            "/payments",
            "Payments",
            "payment",
          ],
        ]
      : isAdmin
        ? [
            [
              "/admin",
              "Dashboard",
              "dashboard",
            ],

            [
              "/admin/users-roles",
              "Users & Roles",
              "admin",
            ],

            [
              "/admin/vendors",
              "Vendor Access",
              "po",
            ],

            [
              "/integration",
              "Integration Support",
              "integration",
            ],

            [
              "/admin/audit",
              "Audit Logs",
              "history",
            ],

            [
              "/admin/system",
              "System Configuration",
              "support",
            ],
          ]
        : isFinance
          ? [
              [
                "/internal",
                "Dashboard",
                "dashboard",
              ],
              ...(user.permissions.includes("INVOICE.VIEW_ALL")
                ? [[
                    "/invoices",
                    "Invoices",
                    "invoice",
                  ] as MenuItem]
                : []),
              ...(user.permissions.includes("PO.VIEW")
                ? [[
                    "/purchase-orders",
                    "Purchase Orders",
                    "po",
                  ] as MenuItem]
                : []),
              ...(user.permissions.includes("GRN.VIEW")
                ? [[
                    "/grns",
                    "GRNs",
                    "grn",
                  ] as MenuItem]
                : []),
              ...(user.permissions.includes("PAYMENT.VIEW")
                ? [[
                    "/payments",
                    "Payments",
                    "payment",
                  ] as MenuItem]
                : []),
              ...(user.permissions.includes("INTEGRATION.VIEW")
                ? [["/integration", "Integration Support", "integration"] as MenuItem]
                : []),
            ]
          : isSupplyChain
            ? [
                [
                  "/internal",
                  "Dashboard",
                  "dashboard",
                ],
                [
                  "/supply-chain/vendors",
                  "Vendor Access",
                  "admin",
                ],
                ...(user.permissions.includes("PO.VIEW")
                  ? [[
                      "/supply-chain/purchase-orders",
                      "Recent Purchase Orders",
                      "po",
                    ] as MenuItem]
                  : []),
                ...(user.permissions.includes("GRN.VIEW")
                  ? [[
                      "/supply-chain/grns",
                      "Recent GRNs",
                      "grn",
                    ] as MenuItem]
                  : []),
                ...(user.permissions.includes("VENDOR.MANAGE")
                  ? [[
                      "/supply-chain/vendor-requests",
                      "Pending Vendor Requests",
                      "support",
                    ] as MenuItem]
                  : []),
                ...(user.permissions.includes("VENDOR.VIEW")
                  ? [[
                      "/supply-chain/onboarded-vendors",
                      "Onboarded Vendors",
                      "user",
                    ] as MenuItem]
                  : []),
                ...(user.permissions.includes("INTEGRATION.VIEW")
                  ? [["/integration", "Integration Support", "integration"] as MenuItem]
                  : []),
              ]
            : [
                [
                  "/internal",
                  "Dashboard",
                  "dashboard",
                ],
                ...(user.permissions.includes("INTEGRATION.VIEW")
                  ? [["/integration", "Integration Support", "integration"] as MenuItem]
                  : []),
              ];

  // ============================================================
  // NOTIFICATIONS
  // ============================================================

  const loadNotifications =
    async () => {
      try {
        setNotificationLoading(
          true
        );

        const response =
          await api.get(
            "/notifications"
          );

        const data =
          Array.isArray(
            response.data
          )
            ? response.data
            : response.data
                ?.items ?? [];

        setNotifications(
          data.slice(0, 5)
        );
      } catch {
        setNotifications([]);
      } finally {
        setNotificationLoading(
          false
        );
      }
    };

  const handleNotificationClick =
    async () => {
      const nextState =
        !notificationOpen;

      setNotificationOpen(
        nextState
      );

      if (nextState) {
        await loadNotifications();
      }
    };

  const markNotificationRead =
    async (
      notification:
        NotificationItem
    ) => {
      if (
        notification.isRead
      ) {
        return;
      }

      try {
        await api.put(
          `/notifications/${notification.id}/read`
        );

        setNotifications(
          (current) =>
            current.map(
              (item) =>
                item.id ===
                notification.id
                  ? {
                      ...item,
                      isRead: true,
                    }
                  : item
            )
        );

        setUnreadCount(
          (current) =>
            Math.max(
              0,
              current - 1
            )
        );
      } catch {
        // Ignore update errors.
      }
    };

  const markAllRead =
    async () => {
      try {
        await api.put(
          "/notifications/read-all"
        );

        setNotifications(
          (current) =>
            current.map(
              (item) => ({
                ...item,
                isRead: true,
              })
            )
        );

        setUnreadCount(0);
      } catch {
        // Ignore update errors.
      }
    };

  const formatNotificationTime =
    (value: string) => {
      if (!value) {
        return "";
      }

      const date =
        new Date(value);

      if (
        Number.isNaN(
          date.getTime()
        )
      ) {
        return "";
      }

      return date.toLocaleString();
    };

  // ============================================================
  // PROFILE
  // ============================================================

  const handleProfileClick =
    async () => {
      if (!isVendor) {
        return;
      }

      const nextState =
        !profileOpen;

      setProfileOpen(
        nextState
      );

      setNotificationOpen(
        false
      );

      if (
        nextState &&
        !supplier
      ) {
        try {
          setProfileLoading(
            true
          );

          const data =
            await getMySupplier();

          setSupplier(
            data
          );
        } catch {
          setSupplier(
            null
          );
        } finally {
          setProfileLoading(
            false
          );
        }
      }
    };

  // ============================================================
  // RENDER
  // ============================================================

  return (
    <div
      className={`portal-shell ${
        sidebarPinnedOpen
          ? "sidebar-pinned-open"
          : "sidebar-pinned-closed"
      }`}
    >
      <aside
        className={`portal-sidebar ${
          sidebarExpanded
            ? "expanded"
            : "collapsed"
        }`}
        onMouseEnter={() =>
          setSidebarHovered(true)
        }
        onMouseLeave={() =>
          setSidebarHovered(false)
        }
      >
        <button
          type="button"
          className="sidebar-toggle"
          onClick={() =>
            setSidebarPinnedOpen(
              (current) =>
                !current
            )
          }
          aria-label={
            sidebarPinnedOpen
              ? "Collapse sidebar"
              : "Keep sidebar open"
          }
          title={
            sidebarPinnedOpen
              ? "Collapse sidebar"
              : "Keep sidebar open"
          }
        >
          <svg
            className="sidebar-toggle-icon"
            viewBox="0 0 24 24"
            aria-hidden="true"
            focusable="false"
          >
            <rect
              x="3"
              y="4"
              width="18"
              height="16"
              rx="3"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.8"
            />
            <path
              d="M9 4v16"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
            />
          </svg>
        </button>

        <div className="pc-brand">
          <img
            src="/pakistan-cables-logo.png"
            alt="Pakistan Cables"
          />

          <div>
            <strong>
              Pakistan Cables
            </strong>

            <span>
              Business Partner&apos;s Portal
            </span>
          </div>
        </div>

        <nav>
          {items.map(
            ([
              to,
              label,
              icon,
            ]) => (
              <NavLink
                key={to}
                to={to}
                end
                title={
                  sidebarExpanded
                    ? undefined
                    : label
                }
                className={({
                  isActive,
                }) =>
                  isActive
                    ? "side-link active"
                    : "side-link"
                }
              >
                <Icon
                  name={icon}
                />

                <span>
                  {label}
                </span>
              </NavLink>
            )
          )}
        </nav>

        <button
          type="button"
          className="side-link side-logout"
          title={
            sidebarExpanded
              ? undefined
              : "Logout"
          }
          onClick={() => {
            logout();

            navigate(
              "/login"
            );
          }}
        >
          <Icon name="logout" />

          <span>
            Logout
          </span>
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
                  : isFinance
                    ? "Finance / AP Dashboard"
                    : isSupplyChain
                      ? "Supply Chain Dashboard"
                      : "Internal Portal"}
            </h1>

            <p>
              Welcome back,{" "}
              {user.fullName}.
            </p>
          </div>

          <div className="top-actions">
            <span className="role-pill">
              {user.userType}
            </span>

            <div
              className="notification-wrapper"
              ref={notificationRef}
            >
              <button
                type="button"
                className={`bell-btn ${
                  notificationOpen
                    ? "open"
                    : ""
                }`}
                onClick={
                  handleNotificationClick
                }
                aria-label="Notifications"
                aria-expanded={
                  notificationOpen
                }
              >
                <Icon name="bell" />

                {unreadCount >
                  0 && (
                  <b>
                    {unreadCount >
                    99
                      ? "99+"
                      : unreadCount}
                  </b>
                )}
              </button>

              {notificationOpen && (
                <div className="notification-popup">
                  <div className="notification-popup-header">
                    <div>
                      <strong>
                        Notifications
                      </strong>

                      <span>
                        {unreadCount}{" "}
                        unread
                      </span>
                    </div>

                    {unreadCount >
                      0 && (
                      <button
                        type="button"
                        className="notification-mark-read"
                        onClick={
                          markAllRead
                        }
                      >
                        Mark all read
                      </button>
                    )}
                  </div>

                  <div className="notification-popup-body">
                    {notificationLoading ? (
                      <div className="notification-empty">
                        Loading
                        notifications...
                      </div>
                    ) : notifications.length ===
                      0 ? (
                      <div className="notification-empty">
                        No notifications
                        yet.
                      </div>
                    ) : (
                      notifications.map(
                        (
                          notification
                        ) => (
                          <button
                            key={
                              notification.id
                            }
                            type="button"
                            className={`notification-item ${
                              notification.isRead
                                ? ""
                                : "unread"
                            }`}
                            onClick={() =>
                              markNotificationRead(
                                notification
                              )
                            }
                          >
                            <span className="notification-status-dot" />

                            <div className="notification-content">
                              <strong>
                                {
                                  notification.title
                                }
                              </strong>

                              <p>
                                {
                                  notification.message
                                }
                              </p>

                              <small>
                                {formatNotificationTime(
                                  notification.createdAt
                                )}
                              </small>
                            </div>
                          </button>
                        )
                      )
                    )}
                  </div>

                  <button
                    type="button"
                    className="notification-see-all"
                    onClick={() => {
                      setNotificationOpen(
                        false
                      );

                      navigate(
                        "/notifications"
                      );
                    }}
                  >
                    See All
                    Notifications

                    <span>
                      →
                    </span>
                  </button>
                </div>
              )}
            </div>

            {isVendor ? (
              <div
                className="vendor-profile-wrapper"
                ref={profileRef}
              >
                <button
                  type="button"
                  className={`user-chip vendor-profile-trigger ${
                    profileOpen
                      ? "open"
                      : ""
                  }`}
                  onClick={
                    handleProfileClick
                  }
                  aria-label="Vendor profile"
                  aria-expanded={
                    profileOpen
                  }
                >
                  <span>
                    {user.fullName.charAt(
                      0
                    )}
                  </span>

                  <strong>
                    {user.fullName}
                  </strong>
                </button>

                {profileOpen && (
                  <div className="vendor-profile-popup">
                    <div className="vendor-profile-popup-head">
                      <strong>
                        Vendor Profile
                      </strong>

                      <button
                        type="button"
                        onClick={() =>
                          setProfileOpen(
                            false
                          )
                        }
                        aria-label="Close vendor profile"
                      >
                        ×
                      </button>
                    </div>

                    {profileLoading ? (
                      <div className="vendor-profile-popup-loading">
                        Loading vendor
                        profile...
                      </div>
                    ) : (
                      <dl>
                        <dt>
                          Vendor Name
                        </dt>

                        <dd>
                          {supplier?.vendorName ||
                            user.fullName ||
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
                          Supplier Number
                        </dt>

                        <dd>
                          {supplier?.supplierNumber ||
                            "-"}
                        </dd>

                        <dt>
                          Site Code
                        </dt>

                        <dd>
                          {supplier?.vendorSiteCode ||
                            "-"}
                        </dd>

                        <dt>
                          Operating Unit
                        </dt>

                        <dd>
                          {supplier?.operatingUnit ||
                            "-"}
                        </dd>

                        <dt>
                          Email
                        </dt>

                        <dd>
                          {supplier?.email ||
                            user.email ||
                            "-"}
                        </dd>

                        <dt>
                          Phone
                        </dt>

                        <dd>
                          {supplier?.phone ||
                            "-"}
                        </dd>
                      </dl>
                    )}
                  </div>
                )}
              </div>
            ) : (
              <div className="user-chip">
                <span>
                  {user.fullName.charAt(
                    0
                  )}
                </span>

                <strong>
                  {user.fullName}
                </strong>
              </div>
            )}
          </div>
        </header>

        <Outlet />
      </section>
    </div>
  );
}
