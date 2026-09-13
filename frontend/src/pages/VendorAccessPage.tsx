import {
  useEffect,
  useMemo,
  useState,
} from "react";
import type { FormEvent } from "react";

import {
  api,
  getApiErrorMessage,
} from "../api/client";

import "./UsersRolesPage.css";

type ExistingVendorAccess = {
  vendorId: string;
  vendorCode: string;
  vendorName: string;
  oracleVendorId: string | null;

  userId: string;
  userName: string;
  email: string;

  userActive: boolean;
  accessActive: boolean;
  isPrimary: boolean;
  passwordSetupCompleted: boolean;

  lastLoginAt?: string | null;
  failedLoginAttempts?: number;
  lockoutUntil?: string | null;
  isLocked?: boolean;
};

type OracleVendor = {
  vendorId: string;
  supplierNumber: string;
  vendorName: string;

  vendorSiteId?: string | null;
  vendorSiteCode?: string | null;

  orgId?: string | null;
  operatingUnit?: string | null;

  email?: string | null;
  phone?: string | null;

  oracleEmail?: string | null;
  oraclePhone?: string | null;

  portalEmail?: string | null;
  portalPhone?: string | null;

  taxNumber?: string | null;
  vendorType?: string | null;

  addressLine1?: string | null;
  addressLine2?: string | null;
  addressLine3?: string | null;

  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
};

type VendorEditForm = {
  fullName: string;
  email: string;
};

type GrantResponse = {
  accessGranted: boolean;
  emailSent: boolean;

  vendorId: string;
  userId: string;

  oracleVendorId: number;

  vendorName: string;
  email: string;

  role: string;

  devSetupUrl?: string | null;

  message: string;
};

function formatDateTime(
  value?: string | null
) {
  if (!value) {
    return "—";
  }

  const date =
    new Date(value);

  if (
    Number.isNaN(
      date.getTime()
    )
  ) {
    return "—";
  }

  return date.toLocaleString(
    "en-GB",
    {
      day: "2-digit",
      month: "short",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      hour12: true,
    }
  );
}

function getVendorAddress(
  vendor: OracleVendor
) {
  const parts = [
    vendor.addressLine1,
    vendor.addressLine2,
    vendor.addressLine3,
    vendor.city,
    vendor.state,
    vendor.postalCode,
    vendor.country,
  ]
    .map(
      (value) =>
        value?.trim()
    )
    .filter(Boolean);

  return parts.length
    ? parts.join(", ")
    : "-";
}

function getAccountStatus(row: ExistingVendorAccess) {
  if (row.isLocked) {
    return "Locked";
  }

  if (!row.accessActive || !row.userActive) {
    return "Disabled";
  }

  return "Enabled";
}

function csvEscape(value: unknown) {
  const text = String(value ?? "");
  return `"${text.replaceAll('"', '""')}"`;
}

export function VendorAccessPage() {
  // ============================================================
  // EXISTING ACCESS
  // ============================================================

  const [
    accessRows,
    setAccessRows,
  ] =
    useState<
      ExistingVendorAccess[]
    >([]);

  const [
    loading,
    setLoading,
  ] =
    useState(true);

  const [
    pageError,
    setPageError,
  ] =
    useState("");

  const [
    pageSuccess,
    setPageSuccess,
  ] =
    useState("");


  // ============================================================
  // ADD VENDOR DRAWER
  // ============================================================

  const [
    drawerOpen,
    setDrawerOpen,
  ] =
    useState(false);

  const [
    search,
    setSearch,
  ] =
    useState("");

  const [
    oracleVendors,
    setOracleVendors,
  ] =
    useState<
      OracleVendor[]
    >([]);

  const [
    selectedVendor,
    setSelectedVendor,
  ] =
    useState<
      OracleVendor | null
    >(null);

  const [
    oracleLoading,
    setOracleLoading,
  ] =
    useState(false);

  const [
    drawerError,
    setDrawerError,
  ] =
    useState("");

  const [
    granting,
    setGranting,
  ] =
    useState(false);

  const [
    devSetupUrl,
    setDevSetupUrl,
  ] =
    useState("");

  // ============================================================
  // CONTACT EDIT
  // ============================================================

  const [
    contactEdit,
    setContactEdit,
  ] =
    useState(false);

  const [
    contactEmail,
    setContactEmail,
  ] =
    useState("");

  const [
    contactPhone,
    setContactPhone,
  ] =
    useState("");

  const [
    contactSaving,
    setContactSaving,
  ] =
    useState(false);

  // ============================================================
  // ROW ACTION
  // ============================================================

  const [
    openMenuVendorId,
    setOpenMenuVendorId,
  ] =
    useState<
      string | null
    >(null);

  const [
    deleteRow,
    setDeleteRow,
  ] =
    useState<
      ExistingVendorAccess | null
    >(null);

  const [
    deleting,
    setDeleting,
  ] =
    useState(false);


  const [
    statusFilter,
    setStatusFilter,
  ] = useState("ALL");

  const [
    editRow,
    setEditRow,
  ] = useState<ExistingVendorAccess | null>(null);

  const [
    editForm,
    setEditForm,
  ] = useState<VendorEditForm>({
    fullName: "",
    email: "",
  });

  const [
    editSaving,
    setEditSaving,
  ] = useState(false);

  const filteredAccessRows = useMemo(() => {
    if (statusFilter === "ALL") {
      return accessRows;
    }

    return accessRows.filter(
      (row) => getAccountStatus(row).toUpperCase() === statusFilter
    );
  }, [accessRows, statusFilter]);

  // ============================================================
  // LOAD EXISTING
  // ============================================================

  async function loadExistingAccess() {
    setLoading(true);

    setPageError("");

    try {
      const response = await api.get("/vendor-access");

      const data =
        Array.isArray(
          response.data
        )
          ? response.data
          : response.data
              ?.items ?? [];

      setAccessRows(data);
    } catch (error) {
      setPageError(
        getApiErrorMessage(
          error
        )
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadExistingAccess();
  }, []);

  // ============================================================
  // CLOSE ROW ACTION MENU ON OUTSIDE CLICK
  // ============================================================

  useEffect(() => {
    function handleOutsideActionMenuClick(
      event: MouseEvent
    ) {
      const target =
        event.target as HTMLElement;

      if (
        !target.closest(
          ".vendor-row-menu"
        )
        &&
        !target.closest(
          ".vendor-more-btn"
        )
      ) {
        setOpenMenuVendorId(
          null
        );
      }
    }

    document.addEventListener(
      "mousedown",
      handleOutsideActionMenuClick
    );

    function closeActionMenu() {
      setOpenMenuVendorId(null);
    }

    window.addEventListener("scroll", closeActionMenu, true);

    return () => {
      document.removeEventListener(
        "mousedown",
        handleOutsideActionMenuClick
      );
      window.removeEventListener("scroll", closeActionMenu, true);
    };
  }, []);

  // ============================================================
  // DRAWER
  // ============================================================

  function openDrawer() {
    setDrawerOpen(true);

    setSearch("");

    setOracleVendors([]);

    setSelectedVendor(
      null
    );

    setDrawerError("");

    setDevSetupUrl("");

    setContactEdit(false);
  }

  function closeDrawer() {
    if (
      granting
      ||
      contactSaving
    ) {
      return;
    }

    setDrawerOpen(false);

    setSearch("");

    setSelectedVendor(
      null
    );

    setOracleVendors([]);

    setDrawerError("");

    setDevSetupUrl("");

    setContactEdit(false);
  }

  // ============================================================
  // ORACLE SEARCH
  //
  // 300ms debounce
  // ============================================================

  useEffect(() => {
    if (!drawerOpen) {
      return;
    }

    let cancelled =
      false;

    const timer =
      window.setTimeout(
        async () => {
          setOracleLoading(
            true
          );

          setDrawerError("");

          try {
            const response =
              await api.get<
                OracleVendor[]
              >(
                "/admin/vendor-access/oracle-vendors",
                {
                  params: {
                    search:
                      search.trim()
                        ? search.trim()
                        : undefined,
                  },
                }
              );

            if (!cancelled) {
              setOracleVendors(
                Array.isArray(
                  response.data
                )
                  ? response.data
                  : []
              );
            }
          } catch (error) {
            if (!cancelled) {
              setOracleVendors(
                []
              );

              setDrawerError(
                getApiErrorMessage(
                  error
                )
              );
            }
          } finally {
            if (!cancelled) {
              setOracleLoading(
                false
              );
            }
          }
        },
        300
      );

    return () => {
      cancelled = true;

      window.clearTimeout(
        timer
      );
    };
  }, [
    drawerOpen,
    search,
  ]);

  // ============================================================
  // ENABLED ORACLE IDs
  // ============================================================

  const enabledOracleIds =
    useMemo(
      () =>
        new Set(
          accessRows
            .filter(
              (row) =>
                row.accessActive
            )
            .map(
              (row) =>
                String(
                  row.oracleVendorId
                  ?? ""
                )
            )
        ),
      [accessRows]
    );

  // ============================================================
  // SELECT VENDOR
  // ============================================================

  function selectVendor(
    vendor: OracleVendor
  ) {
    setSelectedVendor(
      vendor
    );

    setDrawerError("");

    setDevSetupUrl("");

    setContactEdit(false);

    setContactEmail(
      vendor.portalEmail
      ??
      vendor.email
      ??
      ""
    );

    setContactPhone(
      vendor.portalPhone
      ??
      vendor.phone
      ??
      ""
    );
  }

  // ============================================================
  // SAVE PORTAL CONTACT
  // ============================================================

  async function saveContact() {
    if (!selectedVendor) {
      return;
    }

    const oracleVendorId =
      Number(
        selectedVendor.vendorId
      );

    if (
      Number.isNaN(
        oracleVendorId
      )
    ) {
      setDrawerError(
        "Invalid Oracle Vendor ID."
      );

      return;
    }

    setContactSaving(
      true
    );

    setDrawerError("");

    try {
      await api.put(
        "/admin/vendor-access/portal-contact",
        {
          oracleVendorId,

          email:
            contactEmail.trim()
            || null,

          phone:
            contactPhone.trim()
            || null,
        }
      );

      const updated: OracleVendor =
        {
          ...selectedVendor,

          email:
            contactEmail.trim()
            ||
            selectedVendor
              .oracleEmail
            ||
            null,

          phone:
            contactPhone.trim()
            ||
            selectedVendor
              .oraclePhone
            ||
            null,

          portalEmail:
            contactEmail.trim()
            ||
            null,

          portalPhone:
            contactPhone.trim()
            ||
            null,
        };

      setSelectedVendor(
        updated
      );

      setOracleVendors(
        (current) =>
          current.map(
            (vendor) =>
              vendor.vendorId ===
              updated.vendorId
                ? updated
                : vendor
          )
      );

      setContactEdit(
        false
      );

      setPageSuccess(
        "Vendor portal contact details saved."
      );
    } catch (error) {
      setDrawerError(
        getApiErrorMessage(
          error
        )
      );
    } finally {
      setContactSaving(
        false
      );
    }
  }

  // ============================================================
  // GRANT
  // ============================================================

  async function grantAccess() {
    if (!selectedVendor) {
      setDrawerError(
        "Select an Oracle vendor first."
      );

      return;
    }

    if (
      !selectedVendor.email
        ?.trim()
    ) {
      setDrawerError(
        "Add an email address before granting portal access."
      );

      return;
    }

    const oracleVendorId =
      Number(
        selectedVendor.vendorId
      );

    if (
      Number.isNaN(
        oracleVendorId
      )
      ||
      oracleVendorId <= 0
    ) {
      setDrawerError(
        "Invalid Oracle Vendor ID."
      );

      return;
    }

    setGranting(true);

    setDrawerError("");

    setDevSetupUrl("");

    try {
      const response =
        await api.post<
          GrantResponse
        >(
          "/admin/vendor-access/grant",
          {
            oracleVendorId,
          }
        );

      setPageSuccess(
        response.data.message
      );

      await loadExistingAccess();

      if (
        response.data
          .devSetupUrl
      ) {
        setDevSetupUrl(
          response.data
            .devSetupUrl
        );
      } else {
        closeDrawer();
      }
    } catch (error) {
      setDrawerError(
        getApiErrorMessage(
          error
        )
      );
    } finally {
      setGranting(false);
    }
  }

  // ============================================================
  // EDIT VENDOR USER / EXPORT
  // ============================================================

  function openEditVendor(row: ExistingVendorAccess) {
    setOpenMenuVendorId(null);
    setPageError("");
    setPageSuccess("");
    setEditRow(row);
    setEditForm({
      fullName: row.userName || row.vendorName,
      email: row.email || "",
    });
  }

  async function saveVendorUser(event: FormEvent) {
    event.preventDefault();

    if (!editRow) return;

    const fullName = editForm.fullName.trim();
    const email = editForm.email.trim().toLowerCase();

    if (!fullName || !email) {
      setPageError("Full name and email are required.");
      return;
    }

    setEditSaving(true);
    setPageError("");
    setPageSuccess("");

    try {
      const response = await api.put(
        `/vendor-access/${editRow.userId}`,
        { fullName, email }
      );

      setPageSuccess(
        response.data?.message ?? "Vendor user updated successfully."
      );
      setEditRow(null);
      await loadExistingAccess();
    } catch (error) {
      setPageError(getApiErrorMessage(error));
    } finally {
      setEditSaving(false);
    }
  }

  function exportVendorAccess() {
    const headers = [
      "Vendor",
      "Oracle Vendor ID",
      "Email",
      "Status",
      "Last Access",
    ];

    const lines = [
      headers.map(csvEscape).join(","),
      ...filteredAccessRows.map((row) =>
        [
          row.vendorName,
          row.oracleVendorId ?? "",
          row.email,
          getAccountStatus(row),
          formatDateTime(row.lastLoginAt),
        ].map(csvEscape).join(",")
      ),
    ];

    const blob = new Blob(["\uFEFF" + lines.join("\r\n")], {
      type: "text/csv;charset=utf-8;",
    });

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "vendor-access.csv";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  // ============================================================
  // ENABLE / DISABLE
  // ============================================================

  async function toggleAccess(
    row: ExistingVendorAccess
  ) {
    setOpenMenuVendorId(
      null
    );

    setPageError("");

    setPageSuccess("");

    try {
      const response =
        await api.put(
          `/vendor-access/${row.userId}/active`,
          {
            isActive:
              !(row.accessActive && row.userActive),
          }
        );

      setPageSuccess(
        response.data?.message
        ??
        (
          row.accessActive && row.userActive
            ? "Vendor portal access disabled."
            : "Vendor portal access enabled."
        )
      );

      await loadExistingAccess();
      setEditRow(null);
    } catch (error) {
      setPageError(
        getApiErrorMessage(
          error
        )
      );
    }
  }

  async function unlockAccount(
    row: ExistingVendorAccess
  ) {
    setOpenMenuVendorId(null);
    setPageError("");
    setPageSuccess("");

    try {
      const response = await api.post(
        `/vendor-access/${row.userId}/unlock`
      );

      setPageSuccess(
        response.data?.message ??
          "Vendor account unlocked successfully."
      );

      await loadExistingAccess();
      setEditRow(null);
    } catch (error) {
      setPageError(
        getApiErrorMessage(error)
      );
    }
  }

  async function resendSetupEmail(row: ExistingVendorAccess) {
    setOpenMenuVendorId(null);
    if (!window.confirm(`Send a password setup email to ${row.email}?`)) return;
    setPageError("");
    setPageSuccess("");
    try {
      const response = await api.post(`/vendor-access/${row.userId}/resend-setup-email`);
      setPageSuccess(response.data?.message ?? "Password setup email has been sent successfully.");
    } catch {
      setPageError("Email could not be sent. Please check the vendor email address or email configuration.");
    }
  }

  // ============================================================
  // DELETE ACCESS
  // ============================================================

  async function confirmDelete() {
    if (!deleteRow) {
      return;
    }

    setDeleting(true);

    setPageError("");

    setPageSuccess("");

    try {
      const response =
        await api.delete(
          `/vendor-access/${deleteRow.userId}`
        );

      setPageSuccess(
        response.data?.message
        ??
        "Vendor portal access deleted."
      );

      setDeleteRow(null);

      await loadExistingAccess();
    } catch (error) {
      setPageError(
        getApiErrorMessage(
          error
        )
      );
    } finally {
      setDeleting(false);
    }
  }

  // ============================================================
  // RENDER
  // ============================================================

  return (
    <>
      <div className="page-card vendor-access-page">
        <div className="vendor-access-page-header">
          <div>
            <h2>
              Vendor Portal Access
            </h2>

            <p>
              Oracle EBS remains the
              vendor master. This page
              manages portal access and
              portal contact details.
            </p>
          </div>

          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "flex-end",
              gap: 10,
              flexWrap: "wrap",
            }}
          >
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
              aria-label="Filter vendor access by status"
              style={{
                minWidth: 150,
                height: 42,
                border: "1px solid #d7e0db",
                borderRadius: 8,
                padding: "0 12px",
                background: "#fff",
              }}
            >
              <option value="ALL">Status: All</option>
              <option value="ENABLED">Enabled</option>
              <option value="DISABLED">Disabled</option>
              <option value="LOCKED">Locked</option>
            </select>

            <button
              type="button"
              className="secondary-btn"
              onClick={exportVendorAccess}
            >
              Export
            </button>

            <button
              type="button"
              className="primary-btn vendor-add-btn"
              onClick={openDrawer}
            >
              <span>＋</span>
              Add Vendor
            </button>
          </div>
        </div>

        {pageError && (
          <div className="form-error">
            {pageError}
          </div>
        )}

        {pageSuccess && (
          <div className="form-success">
            {pageSuccess}
          </div>
        )}

        <div className="vendor-existing-section">
          <div className="vendor-existing-heading">
            <div>
              <h3>Existing Vendor Access</h3>
              <p>View and manage portal access for existing vendors.</p>
            </div>


          </div>

          {loading ? (
            <div className="empty-state">
              Loading vendor access...
            </div>
          ) : (
            <div className="vendor-access-table-wrap">
              <table className="data-table vendor-access-table">
                <thead>
                  <tr>
                    <th>Vendor</th>

                    <th>
                      Oracle Vendor ID
                    </th>

                    <th>Email</th>

                    <th>Status</th>

                    <th>
                      Last Access
                    </th>

                    <th>Action</th>
                  </tr>
                </thead>

                <tbody>
                  {filteredAccessRows.map(
                    (row) => (
                      <tr
                        key={`${row.vendorId}-${row.userId}`}
                      >
                        <td>
                          <strong>
                            {
                              row.vendorName
                            }
                          </strong>
                        </td>

                        <td>
                          {row.oracleVendorId
                          ||
                          "-"}
                        </td>

                        <td>
                          {row.email}
                        </td>

                        <td>
                          <span
                            className={`status ${
                              getAccountStatus(row) === "Locked"
                                ? "orange"
                                : getAccountStatus(row) === "Enabled"
                                  ? "green"
                                  : "red"
                            }`}
                          >
                            {getAccountStatus(row)}
                          </span>
                        </td>

                        <td>
                          {formatDateTime(
                            row.lastLoginAt
                          )}
                        </td>

                        <td className="vendor-action-cell">
                          <button
                            type="button"
                            className="vendor-more-btn"
                            onClick={(
                              event
                            ) => {
                              event.stopPropagation();

                              setOpenMenuVendorId(
                                (
                                  current
                                ) =>
                                  current ===
                                  row.vendorId
                                    ? null
                                    : row.vendorId
                              );
                            }}
                          >
                            ⋮
                          </button>

                          {openMenuVendorId ===
                            row.vendorId && (
                            <div
                              className="vendor-row-menu"
                              onMouseDown={(event) => event.stopPropagation()}
                            >
                              <button
                                type="button"
                                onClick={() => openEditVendor(row)}
                              >
                                Edit
                              </button>
                            </div>
                          )}
                        </td>
                      </tr>
                    )
                  )}

                  {!filteredAccessRows.length && (
                    <tr>
                      <td
                        colSpan={6}
                        className="empty"
                      >
                        No vendor access
                        records found.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>

              <div className="vendor-table-footer">
                Showing{" "}
                {filteredAccessRows.length}{" "}
                {filteredAccessRows.length ===
                1
                  ? "vendor"
                  : "vendors"}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ======================================================
          EDIT VENDOR USER
      ====================================================== */}

      {editRow && (
        <div
          className="admin-modal-backdrop"
          onMouseDown={() => {
            if (!editSaving) setEditRow(null);
          }}
        >
          <div
            className="admin-modal user-modal"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="admin-modal-header">
              <div>
                <h3>Edit Vendor User</h3>
                <p>Manage vendor portal identity and account access.</p>
              </div>

              <button
                type="button"
                className="admin-modal-close"
                onClick={() => setEditRow(null)}
                disabled={editSaving}
              >
                ×
              </button>
            </div>

            <form onSubmit={saveVendorUser} className="admin-modal-body">
              <div className="admin-form-grid">
                <label>
                  Full Name
                  <input
                    value={editForm.fullName}
                    onChange={(event) =>
                      setEditForm({ ...editForm, fullName: event.target.value })
                    }
                    required
                  />
                </label>

                <label>
                  Email
                  <input
                    type="email"
                    value={editForm.email}
                    onChange={(event) =>
                      setEditForm({ ...editForm, email: event.target.value })
                    }
                    required
                  />
                </label>

                <label>
                  User Type
                  <select value="VENDOR" disabled>
                    <option value="VENDOR">VENDOR</option>
                  </select>
                </label>
              </div>

              <div className="admin-form-section">
                <h4>Roles</h4>
                <div className="role-selection-grid">
                  <label className="role-selection">
                    <input type="checkbox" checked readOnly />
                    <span>
                      <strong>Vendor</strong>
                      <small>VENDOR</small>
                    </span>
                  </label>
                </div>
              </div>

              <div className="user-account-status-card">
                <div className="user-account-status-head">
                  <div>
                    <strong>Account Status</strong>
                    <span
                      className={`status ${
                        getAccountStatus(editRow) === "Locked"
                          ? "orange"
                          : getAccountStatus(editRow) === "Enabled"
                            ? "green"
                            : "red"
                      }`}
                    >
                      {getAccountStatus(editRow)}
                    </span>
                  </div>
                </div>

                <div className="user-account-status-grid">
                  <div>
                    <small>Failed Login Attempts</small>
                    <strong>{editRow.failedLoginAttempts ?? 0}</strong>
                  </div>
                  <div>
                    <small>Locked Until</small>
                    <strong>{formatDateTime(editRow.lockoutUntil)}</strong>
                  </div>
                </div>
              </div>

              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: 10,
                  paddingTop: 4,
                }}
              >
                {editRow.isLocked && (
                  <button
                    type="button"
                    className="secondary-btn"
                    onClick={() => unlockAccount(editRow)}
                  >
                    Unlock Account
                  </button>
                )}

                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() => toggleAccess(editRow)}
                >
                  {editRow.accessActive && editRow.userActive
                    ? "Disable Access"
                    : "Enable Access"}
                </button>

                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() => resendSetupEmail(editRow)}
                >
                  Send Password Setup Email
                </button>

                <button
                  type="button"
                  className="danger-btn"
                  onClick={() => {
                    setEditRow(null);
                    setDeleteRow(editRow);
                  }}
                >
                  Delete Access
                </button>
              </div>

              <div className="admin-modal-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() => setEditRow(null)}
                  disabled={editSaving}
                >
                  Cancel
                </button>

                <button
                  type="submit"
                  className="primary-btn"
                  disabled={editSaving}
                >
                  {editSaving ? "Saving..." : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ======================================================
          ADD VENDOR DRAWER
      ====================================================== */}

      {drawerOpen && (
        <div
          className="vendor-drawer-backdrop"
          onMouseDown={
            closeDrawer
          }
        >
          <aside
            className="vendor-drawer"
            onMouseDown={(
              event
            ) =>
              event.stopPropagation()
            }
          >
            <div className="vendor-drawer-header">
              <div>
                <h2>
                  Add Vendor Access
                </h2>

                <p>
                  Search and select an
                  Oracle supplier to
                  configure portal access.
                </p>
              </div>

              <button
                type="button"
                className="vendor-drawer-close"
                onClick={
                  closeDrawer
                }
              >
                ×
              </button>
            </div>

            <div className="vendor-drawer-body">
              <section className="vendor-drawer-step">
                <h4>
                  1. Search Vendor
                </h4>

                <div className="vendor-search-box">
                  <input
                    value={search}
                    onChange={(
                      event
                    ) =>
                      setSearch(
                        event.target
                          .value
                      )
                    }
                    placeholder="Search by vendor name, Oracle Vendor ID, supplier number or email..."
                  />

                  <span>⌕</span>
                </div>
              </section>

              <section className="vendor-drawer-step">
                <h4>
                  2. Select Vendor
                </h4>

                <div className="oracle-vendor-list">
                  {oracleLoading ? (
                    <div className="vendor-drawer-loading">
                      Searching Oracle
                      vendors...
                    </div>
                  ) : (
                    oracleVendors.map(
                      (vendor) => {
                        const enabled =
                          enabledOracleIds.has(
                            String(
                              vendor.vendorId
                            )
                          );

                        const selected =
                          selectedVendor
                            ?.vendorId ===
                          vendor.vendorId;

                        return (
                          <button
                            key={`${vendor.vendorId}-${vendor.vendorSiteId ?? ""}`}
                            type="button"
                            className={`oracle-vendor-row ${
                              selected
                                ? "selected"
                                : ""
                            }`}
                            onClick={() =>
                              selectVendor(
                                vendor
                              )
                            }
                          >
                            <span
                              className={`vendor-radio ${
                                selected
                                  ? "checked"
                                  : ""
                              }`}
                            />

                            <div>
                              <strong>
                                {
                                  vendor.vendorName
                                }
                              </strong>

                              <small>
                                Oracle Vendor
                                ID:{" "}
                                {
                                  vendor.vendorId
                                }
                              </small>
                            </div>

                            {enabled && (
                              <span className="vendor-enabled-label">
                                Already
                                Enabled
                              </span>
                            )}
                          </button>
                        );
                      }
                    )
                  )}

                  {!oracleLoading &&
                    !oracleVendors
                      .length && (
                    <div className="vendor-drawer-loading">
                      No Oracle vendors
                      found.
                    </div>
                  )}
                </div>
              </section>

              {selectedVendor && (
                <section className="vendor-drawer-step vendor-details-section">
                  <div className="vendor-details-title-row">
                    <h4>
                      3. Vendor Details
                    </h4>

                    {!contactEdit && (
                      <button
                        type="button"
                        className="vendor-edit-contact-btn"
                        onClick={() => {
                          setContactEmail(
                            selectedVendor
                              .portalEmail
                            ??
                            selectedVendor
                              .email
                            ??
                            ""
                          );

                          setContactPhone(
                            selectedVendor
                              .portalPhone
                            ??
                            selectedVendor
                              .phone
                            ??
                            ""
                          );

                          setContactEdit(
                            true
                          );
                        }}
                      >
                        Edit Contact
                      </button>
                    )}
                  </div>

                  <dl className="vendor-detail-grid">
                    <dt>
                      Vendor Name
                    </dt>

                    <dd>
                      {
                        selectedVendor.vendorName
                      }
                    </dd>

                    <dt>
                      Oracle Vendor ID
                    </dt>

                    <dd>
                      {
                        selectedVendor.vendorId
                      }
                    </dd>

                    <dt>
                      Supplier Number
                    </dt>

                    <dd>
                      {selectedVendor.supplierNumber
                      ||
                      "-"}
                    </dd>

                    <dt>
                      Vendor Type
                    </dt>

                    <dd>
                      {selectedVendor.vendorType
                      ||
                      "Supplier"}
                    </dd>

                    <dt>
                      Operating Unit
                    </dt>

                    <dd>
                      {selectedVendor.operatingUnit
                      ||
                      "-"}
                    </dd>

                    <dt>
                      Country
                    </dt>

                    <dd>
                      {selectedVendor.country
                      ||
                      "-"}
                    </dd>

                    <dt>
                      Address
                    </dt>

                    <dd>
                      {getVendorAddress(
                        selectedVendor
                      )}
                    </dd>

                    {!contactEdit && (
                      <>
                        <dt>
                          Phone
                        </dt>

                        <dd>
                          {selectedVendor.phone
                          ||
                          "-"}
                        </dd>

                        <dt>
                          Email
                        </dt>

                        <dd>
                          {selectedVendor.email
                          ||
                          "-"}
                        </dd>
                      </>
                    )}

                    <dt>
                      Status in Oracle
                    </dt>

                    <dd>
                      <span className="status green">
                        Available
                      </span>
                    </dd>
                  </dl>

                  {contactEdit && (
                    <div className="vendor-contact-edit-card">
                      <h5>
                        Portal Contact Details
                      </h5>

                      <p>
                        These values are
                        stored only in the
                        portal. Oracle
                        supplier data will
                        not be changed.
                      </p>

                      <label>
                        Email Address

                        <input
                          type="email"
                          value={
                            contactEmail
                          }
                          onChange={(
                            event
                          ) =>
                            setContactEmail(
                              event
                                .target
                                .value
                            )
                          }
                          placeholder="vendor@example.com"
                        />
                      </label>

                      <label>
                        Contact Number

                        <input
                          value={
                            contactPhone
                          }
                          onChange={(
                            event
                          ) =>
                            setContactPhone(
                              event
                                .target
                                .value
                            )
                          }
                          placeholder="+92..."
                        />
                      </label>

                      <div className="vendor-contact-edit-actions">
                        <button
                          type="button"
                          className="secondary-btn"
                          disabled={
                            contactSaving
                          }
                          onClick={() =>
                            setContactEdit(
                              false
                            )
                          }
                        >
                          Cancel
                        </button>

                        <button
                          type="button"
                          className="primary-btn"
                          disabled={
                            contactSaving
                          }
                          onClick={
                            saveContact
                          }
                        >
                          {contactSaving
                            ? "Saving..."
                            : "Save Contact"}
                        </button>
                      </div>
                    </div>
                  )}

                  {!contactEdit &&
                    selectedVendor.email && (
                    <div className="vendor-email-notice">
                      <span>
                        ✉
                      </span>

                      <div>
                        <strong>
                          Password setup
                          email will be
                          sent to{" "}
                          {
                            selectedVendor.email
                          }
                        </strong>

                        <p>
                          The vendor can
                          set a password
                          and then sign in
                          using this email
                          address.
                        </p>
                      </div>
                    </div>
                  )}

                  {!contactEdit &&
                    !selectedVendor.email && (
                    <div className="form-error">
                      This supplier does
                      not have an email
                      address. Click
                      <strong>
                        {" "}
                        Edit Contact{" "}
                      </strong>
                      and add a portal
                      email before
                      granting access.
                    </div>
                  )}
                </section>
              )}

              {drawerError && (
                <div className="form-error vendor-drawer-error">
                  {drawerError}
                </div>
              )}

              {devSetupUrl && (
                <div className="vendor-dev-link">
                  <strong>
                    Development Mode
                  </strong>

                  <p>
                    SMTP is disabled.
                    Use this password
                    setup link:
                  </p>

                  <a
                    href={
                      devSetupUrl
                    }
                    target="_blank"
                    rel="noreferrer"
                  >
                    Open Password Setup
                  </a>
                </div>
              )}
            </div>

            <div className="vendor-drawer-footer">
              <button
                type="button"
                className="secondary-btn"
                onClick={
                  closeDrawer
                }
                disabled={
                  granting
                }
              >
                Cancel
              </button>

              <button
                type="button"
                className="primary-btn vendor-grant-email-btn"
                onClick={
                  grantAccess
                }
                disabled={
                  granting
                  ||
                  contactEdit
                  ||
                  !selectedVendor
                  ||
                  !selectedVendor.email
                }
              >
                {granting
                  ? "Granting Access..."
                  : "Grant Portal Access & Send Email"}
              </button>
            </div>
          </aside>
        </div>
      )}

      {/* ======================================================
          DELETE CONFIRMATION
      ====================================================== */}

      {deleteRow && (
        <div
          className="vendor-delete-backdrop"
          onMouseDown={() => {
            if (!deleting) {
              setDeleteRow(
                null
              );
            }
          }}
        >
          <div
            className="vendor-delete-modal"
            onMouseDown={(
              event
            ) =>
              event.stopPropagation()
            }
          >
            <div className="vendor-delete-icon">
              !
            </div>

            <h3>
              Delete Vendor Portal
              Access?
            </h3>

            <p>
              Portal access for{" "}
              <strong>
                {
                  deleteRow.vendorName
                }
              </strong>{" "}
              will be removed.
            </p>

            <div className="vendor-delete-warning">
              Oracle supplier data will
              not be deleted or changed.
            </div>

            <div className="vendor-delete-actions">
              <button
                type="button"
                className="secondary-btn"
                disabled={
                  deleting
                }
                onClick={() =>
                  setDeleteRow(
                    null
                  )
                }
              >
                Cancel
              </button>

              <button
                type="button"
                className="danger-btn"
                disabled={
                  deleting
                }
                onClick={
                  confirmDelete
                }
              >
                {deleting
                  ? "Deleting..."
                  : "Delete Access"}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
