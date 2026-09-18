import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { NotificationItem } from "../types";

export function NotificationsPage() {
  const [rows, setRows] = useState<NotificationItem[]>([]);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [deletingAll, setDeletingAll] = useState(false);

  const load = async () => {
    const response = await api.get("/notifications");
    setRows(response.data);
  };

  useEffect(() => {
    void load();
  }, []);

  const markAllRead = async () => {
    await api.put("/notifications/read-all");
    await load();
  };

  const markRead = async (id: string) => {
    setBusyId(id);

    try {
      await api.put(`/notifications/${id}/read`);
      await load();
    } finally {
      setBusyId(null);
    }
  };

  const deleteNotification = async (id: string) => {
    const confirmed = window.confirm(
      "Are you sure you want to delete this notification?"
    );

    if (!confirmed) {
      return;
    }

    setBusyId(id);

    try {
      await api.delete(`/notifications/${id}`);
      setRows((current) =>
        current.filter((notification) => notification.id !== id)
      );
    } finally {
      setBusyId(null);
    }
  };

  const deleteAllNotifications = async () => {
    if (rows.length === 0) {
      return;
    }

    const confirmed = window.confirm(
      "Are you sure you want to delete all notifications?"
    );

    if (!confirmed) {
      return;
    }

    setDeletingAll(true);

    try {
      await api.delete("/notifications");
      setRows([]);
    } finally {
      setDeletingAll(false);
    }
  };

  return (
    <div className="page-card">
      <div className="page-card-head">
        <div>
          <h2>Notifications</h2>
          <p>Portal, invoice and integration notifications.</p>
        </div>

        <div className="notification-page-actions">
          <button
            type="button"
            className="notification-delete-all-btn"
            onClick={deleteAllNotifications}
            disabled={rows.length === 0 || deletingAll}
          >
            {deletingAll ? "Deleting..." : "Delete All"}
          </button>

          <button
            type="button"
            className="secondary-btn"
            onClick={markAllRead}
            disabled={rows.length === 0}
          >
            Mark all as read
          </button>
        </div>
      </div>

      <div className="notification-list">
        {rows.length === 0 ? (
          <div className="empty-state">
            No notifications available.
          </div>
        ) : (
          rows.map((notification) => (
            <div
              className={
                notification.isRead
                  ? "notification-row"
                  : "notification-row unread"
              }
              key={notification.id}
            >
              <div className="notification-row-content">
                <b>{notification.title}</b>
                <p>{notification.message}</p>
                <small>
                  {new Date(notification.createdAt).toLocaleString()}
                </small>
              </div>

              <div className="notification-row-actions">
                {!notification.isRead && (
                  <button
                    type="button"
                    className="table-btn"
                    onClick={() => markRead(notification.id)}
                    disabled={busyId === notification.id}
                  >
                    Mark read
                  </button>
                )}

                <button
                  type="button"
                  className="notification-delete-btn"
                  title="Delete notification"
                  aria-label="Delete notification"
                  onClick={() => deleteNotification(notification.id)}
                  disabled={busyId === notification.id}
                >
                  <svg
                    viewBox="0 0 24 24"
                    aria-hidden="true"
                    focusable="false"
                  >
                    <path
                      d="M9 3h6m-8 4h10m-9 0 .7 13h6.6L16 7M10 10v7m4-7v7"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.8"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    />
                  </svg>
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
