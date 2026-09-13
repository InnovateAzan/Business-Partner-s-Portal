import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import { getAuditLogs } from "../api/portal";
import { exportRowsToCsv } from "../utils/exportCsv";

export function UserManagementPage() {
  const [rows, setRows] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [form, setForm] = useState({
    fullName: "",
    email: "",
    userType: "INTERNAL",
    roleCodes: ["SUPPLY_CHAIN"] as string[],
    password: "",
    isActive: true,
    isSuperAdmin: false
  });
  const [message, setMessage] = useState("");

  const load = () =>
    Promise.all([
      api.get("/admin/users"),
      api.get("/admin/roles")
    ]).then(([u, r]) => {
      setRows(u.data);
      setRoles(r.data);
    });

  useEffect(() => {
    load();
  }, []);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setMessage("");

    try {
      await api.post("/admin/users", form);

      setMessage("User created successfully.");

      setForm({
        ...form,
        fullName: "",
        email: "",
        password: ""
      });

      load();
    } catch (e: any) {
      setMessage(
        e?.response?.data?.message ||
          e.message
      );
    }
  }

  return (
    <div className="page-card">
      <div className="page-card-head">
        <div>
          <h2>User Management</h2>

          <p className="subtext">
            Admin / IT controls portal identities, roles and access.
          </p>
        </div>
      </div>

      <form
        className="admin-user-form"
        onSubmit={create}
      >
        <input
          placeholder="Full Name"
          value={form.fullName}
          onChange={(e) =>
            setForm({
              ...form,
              fullName: e.target.value
            })
          }
          required
        />

        <input
          type="email"
          placeholder="Email"
          value={form.email}
          onChange={(e) =>
            setForm({
              ...form,
              email: e.target.value
            })
          }
          required
        />

        <select
          value={form.userType}
          onChange={(e) =>
            setForm({
              ...form,
              userType: e.target.value
            })
          }
        >
          <option>INTERNAL</option>
          <option>ADMIN</option>
          <option>VENDOR</option>
        </select>

        <select
          value={form.roleCodes[0] || ""}
          onChange={(e) =>
            setForm({
              ...form,
              roleCodes: [e.target.value]
            })
          }
        >
          {roles.map((r) => (
            <option
              key={r.code}
              value={r.code}
            >
              {r.name}
            </option>
          ))}
        </select>

        <input
          type="password"
          placeholder="Temporary Password"
          value={form.password}
          onChange={(e) =>
            setForm({
              ...form,
              password: e.target.value
            })
          }
          required
        />

        <button className="primary-btn">
          Add User
        </button>
      </form>

      {message && (
        <div
          className={
            message.includes("success")
              ? "success-note"
              : "form-error"
          }
        >
          {message}
        </div>
      )}

      <table className="data-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Email</th>
            <th>Type</th>
            <th>Active</th>
            <th>Roles</th>
          </tr>
        </thead>

        <tbody>
          {rows.map((x) => (
            <tr key={x.id}>
              <td>{x.fullName}</td>
              <td>{x.email}</td>
              <td>{x.userType}</td>

              <td>
                <span
                  className={`status ${
                    x.isActive
                      ? "green"
                      : "red"
                  }`}
                >
                  {x.isActive
                    ? "Active"
                    : "Disabled"}
                </span>
              </td>

              <td>
                {x.roles?.join(", ")}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function VendorAccessPage() {
  const [rows, setRows] =
    useState<any[]>([]);

  const load = () =>
    api
      .get("/vendor-access")
      .then((r) =>
        setRows(r.data)
      );

  useEffect(() => {
    load();
  }, []);

  return (
    <div className="page-card">
      <div className="page-card-head">
        <div>
          <h2>
            Vendor Portal Access
          </h2>

          <p>
            Oracle EBS remains the vendor master. This page manages portal access only.
          </p>
        </div>
      </div>

      <div className="success-note">
        New vendors can self-register after Oracle supplier details and registered email are verified by OTP.
      </div>

      <table className="data-table">
        <thead>
          <tr>
            <th>Vendor</th>
            <th>
              Oracle Vendor ID
            </th>
            <th>User</th>
            <th>Email</th>
            <th>Access</th>
            <th>Action</th>
          </tr>
        </thead>

        <tbody>
          {rows.map((x) => (
            <tr
              key={`${x.vendorId}-${x.userId}`}
            >
              <td>
                {x.vendorName}
              </td>

              <td>
                {x.oracleVendorId}
              </td>

              <td>
                {x.fullName}
              </td>

              <td>
                {x.email}
              </td>

              <td>
                <span
                  className={`status ${
                    x.accessActive &&
                    x.isActive
                      ? "green"
                      : "red"
                  }`}
                >
                  {x.accessActive &&
                  x.isActive
                    ? "Enabled"
                    : "Disabled"}
                </span>
              </td>

              <td>
                <button
                  className="table-btn"
                  onClick={async () => {
                    await api.put(
                      `/vendor-access/${x.userId}/active`,
                      {
                        isActive: !(
                          x.accessActive &&
                          x.isActive
                        )
                      }
                    );

                    load();
                  }}
                >
                  {x.accessActive &&
                  x.isActive
                    ? "Disable"
                    : "Enable"}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function AuditPage() {
  const [rows, setRows] =
    useState<any[]>([]);

  const [search, setSearch] =
    useState("");

  const [
    showDateFilter,
    setShowDateFilter
  ] = useState(false);

  const [
    fromDateDraft,
    setFromDateDraft
  ] = useState("");

  const [
    toDateDraft,
    setToDateDraft
  ] = useState("");

  const [fromDate, setFromDate] =
    useState("");

  const [toDate, setToDate] =
    useState("");

  useEffect(() => {
    getAuditLogs()
      .then(setRows)
      .catch(() =>
        setRows([])
      );
  }, []);

  const formatAuditTime = (
    value?: string | null
  ) =>
    value
      ? new Date(
          value
        ).toLocaleString(
          "en-GB",
          {
            day: "2-digit",
            month: "short",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
          }
        )
      : "-";

  const filteredRows =
    useMemo(() => {
      const searchText =
        search
          .trim()
          .toLowerCase();

      return rows.filter(
        (x: any) => {
          const searchableText = [
            x.user,
            x.role,
            x.action,
            x.module,
            x.recordReference,
            x.details
          ]
            .map((value) =>
              String(
                value || ""
              ).toLowerCase()
            )
            .join(" ");

          if (
            searchText &&
            !searchableText.includes(
              searchText
            )
          ) {
            return false;
          }

          if (
            fromDate ||
            toDate
          ) {
            if (!x.createdAt) {
              return false;
            }

            const rowDate =
              new Date(
                x.createdAt
              );

            if (
              Number.isNaN(
                rowDate.getTime()
              )
            ) {
              return false;
            }

            if (fromDate) {
              const startDate =
                new Date(
                  `${fromDate}T00:00:00`
                );

              if (
                rowDate <
                startDate
              ) {
                return false;
              }
            }

            if (toDate) {
              const endDate =
                new Date(
                  `${toDate}T23:59:59.999`
                );

              if (
                rowDate >
                endDate
              ) {
                return false;
              }
            }
          }

          return true;
        }
      );
    }, [
      rows,
      search,
      fromDate,
      toDate
    ]);

  const applyDateFilter =
    () => {
      setFromDate(
        fromDateDraft
      );

      setToDate(
        toDateDraft
      );

      setShowDateFilter(
        false
      );
    };

  const clearDateFilter =
    () => {
      setFromDateDraft("");
      setToDateDraft("");
      setFromDate("");
      setToDate("");

      setShowDateFilter(
        false
      );
    };

  const exportAuditLogs =
    () => {
      exportRowsToCsv(
        "audit-logs.csv",
        [
          "Time",
          "User",
          "Role",
          "Action",
          "Module",
          "Record / Reference",
          "Details"
        ],
        filteredRows.map(
          (x: any) => [
            formatAuditTime(
              x.createdAt
            ),
            x.user ||
              "System",
            x.role ||
              "-",
            x.action ||
              "-",
            x.module ||
              "-",
            x.recordReference ||
              "-",
            x.details ||
              "-"
          ]
        )
      );
    };

  return (
    <div className="page-card audit-log-page">
      <div className="page-card-head">
        <div>
          <h2>
            Audit Logs
          </h2>

          <p className="subtext">
            Business and security activities recorded across the portal.
          </p>
        </div>

        <div
          style={{
            display:
              "flex",
            alignItems:
              "center",
            gap:
              "10px",
            justifyContent:
              "flex-end",
            flexWrap:
              "wrap"
          }}
        >
          <input
            type="text"
            placeholder="Search audit logs..."
            value={search}
            onChange={(e) =>
              setSearch(
                e.target.value
              )
            }
            style={{
              minWidth:
                "220px",
              height:
                "42px",
              padding:
                "0 12px",
              border:
                "1px solid #d7dfdb",
              borderRadius:
                "8px",
              outline:
                "none",
              background:
                "#fff"
            }}
          />

          <div
            style={{
              position:
                "relative"
            }}
          >
            <button
              type="button"
              className="table-btn"
              onClick={() =>
                setShowDateFilter(
                  !showDateFilter
                )
              }
              style={{
                height:
                  "42px",
                padding:
                  "0 16px"
              }}
            >
              Date Filter
            </button>

            {showDateFilter && (
              <div
                style={{
                  position:
                    "absolute",
                  top:
                    "50px",
                  right:
                    0,
                  width:
                    "270px",
                  background:
                    "#ffffff",
                  border:
                    "1px solid #dce4df",
                  borderRadius:
                    "10px",
                  boxShadow:
                    "0 10px 30px rgba(0,0,0,0.12)",
                  padding:
                    "14px",
                  zIndex:
                    100
                }}
              >
                <div
                  style={{
                    marginBottom:
                      "12px"
                  }}
                >
                  <label
                    style={{
                      display:
                        "block",
                      fontSize:
                        "12px",
                      fontWeight:
                        600,
                      marginBottom:
                        "6px"
                    }}
                  >
                    From Date
                  </label>

                  <input
                    type="date"
                    value={
                      fromDateDraft
                    }
                    onChange={(e) =>
                      setFromDateDraft(
                        e.target.value
                      )
                    }
                    style={{
                      width:
                        "100%",
                      height:
                        "40px",
                      boxSizing:
                        "border-box",
                      padding:
                        "0 10px",
                      border:
                        "1px solid #d7dfdb",
                      borderRadius:
                        "8px",
                      background:
                        "#ffffff",
                      outline:
                        "none"
                    }}
                  />
                </div>

                <div
                  style={{
                    marginBottom:
                      "14px"
                  }}
                >
                  <label
                    style={{
                      display:
                        "block",
                      fontSize:
                        "12px",
                      fontWeight:
                        600,
                      marginBottom:
                        "6px"
                    }}
                  >
                    To Date
                  </label>

                  <input
                    type="date"
                    value={
                      toDateDraft
                    }
                    onChange={(e) =>
                      setToDateDraft(
                        e.target.value
                      )
                    }
                    style={{
                      width:
                        "100%",
                      height:
                        "40px",
                      boxSizing:
                        "border-box",
                      padding:
                        "0 10px",
                      border:
                        "1px solid #d7dfdb",
                      borderRadius:
                        "8px",
                      background:
                        "#ffffff",
                      outline:
                        "none"
                    }}
                  />
                </div>

                <div
                  style={{
                    display:
                      "flex",
                    justifyContent:
                      "flex-end",
                    gap:
                      "8px"
                  }}
                >
                  <button
                    type="button"
                    className="table-btn"
                    onClick={
                      clearDateFilter
                    }
                  >
                    Clear
                  </button>

                  <button
                    type="button"
                    className="primary-btn"
                    onClick={
                      applyDateFilter
                    }
                  >
                    Apply
                  </button>
                </div>
              </div>
            )}
          </div>

          <button
            type="button"
            className="primary-btn"
            onClick={
              exportAuditLogs
            }
            disabled={
              !filteredRows.length
            }
          >
            Export
          </button>
        </div>
      </div>

      <div
        style={{
          marginBottom:
            "12px",
          fontSize:
            "12px",
          color:
            "#64748b"
        }}
      >
        Showing{" "}
        {filteredRows.length}{" "}
        of{" "}
        {rows.length}{" "}
        records
      </div>

      <div className="role-table-scroll">
        <table className="data-table audit-log-table">
          <thead>
            <tr>
              <th>Time</th>
              <th>User</th>
              <th>Role</th>
              <th>Action</th>
              <th>Module</th>
              <th>
                Record / Reference
              </th>
              <th>Details</th>
            </tr>
          </thead>

          <tbody>
            {filteredRows.map(
              (x: any) => (
                <tr key={x.id}>
                  <td>
                    {formatAuditTime(
                      x.createdAt
                    )}
                  </td>

                  <td>
                    {x.user ||
                      "System"}
                  </td>

                  <td>
                    {x.role ||
                      "-"}
                  </td>

                  <td>
                    {x.action ||
                      "-"}
                  </td>

                  <td>
                    {x.module ||
                      "-"}
                  </td>

                  <td>
                    {x.recordReference ||
                      "-"}
                  </td>

                  <td>
                    {x.details ||
                      "-"}
                  </td>
                </tr>
              )
            )}

            {!filteredRows.length && (
              <tr>
                <td
                  colSpan={7}
                  className="empty"
                >
                  {rows.length
                    ? "No audit logs match the selected filters."
                    : "No audit logs available."}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function RolesPermissionsPage() {
  const [roles, setRoles] =
    useState<any[]>([]);

  const [
    permissions,
    setPermissions
  ] =
    useState<any[]>([]);

  const [
    selectedRole,
    setSelectedRole
  ] =
    useState<string>("");

  const [
    selected,
    setSelected
  ] =
    useState<string[]>([]);

  const [
    message,
    setMessage
  ] =
    useState("");

  useEffect(() => {
    Promise.all([
      api.get(
        "/admin/roles"
      ),
      api.get(
        "/admin/permissions"
      )
    ]).then(
      ([r, p]) => {
        setRoles(
          r.data
        );

        setPermissions(
          p.data
        );

        if (
          r.data.length
        ) {
          setSelectedRole(
            r.data[0].id
          );
        }
      }
    );
  }, []);

  useEffect(() => {
    if (
      !selectedRole
    ) {
      return;
    }

    api
      .get(
        `/admin/roles/${selectedRole}/permissions`
      )
      .then((r) =>
        setSelected(
          r.data
        )
      );
  }, [
    selectedRole
  ]);

  const role =
    roles.find(
      (r) =>
        r.id ===
        selectedRole
    );

  async function save() {
    setMessage("");

    try {
      await api.put(
        `/admin/roles/${selectedRole}/permissions`,
        {
          permissionIds:
            selected
        }
      );

      setMessage(
        "Permissions saved successfully."
      );
    } catch (e: any) {
      setMessage(
        e?.response?.data
          ?.message ||
          e.message
      );
    }
  }

  return (
    <div className="page-card">
      <div className="page-card-head">
        <div>
          <h2>
            Roles & Permissions
          </h2>

          <p>
            Policy-based access control for portal roles.
          </p>
        </div>
      </div>

      <div className="role-permission-layout">
        <div>
          <label>
            Role
          </label>

          <select
            value={
              selectedRole
            }
            onChange={(e) =>
              setSelectedRole(
                e.target.value
              )
            }
          >
            {roles.map(
              (r) => (
                <option
                  key={
                    r.id
                  }
                  value={
                    r.id
                  }
                >
                  {r.name}
                </option>
              )
            )}
          </select>
        </div>

        <div className="permission-grid">
          {permissions.map(
            (p) => (
              <label
                key={
                  p.id
                }
                className="permission-check"
              >
                <input
                  type="checkbox"
                  disabled={
                    role?.code ===
                    "ADMIN"
                  }
                  checked={
                    role?.code ===
                      "ADMIN" ||
                    selected.includes(
                      p.id
                    )
                  }
                  onChange={(e) =>
                    setSelected(
                      e.target
                        .checked
                        ? [
                            ...selected,
                            p.id
                          ]
                        : selected.filter(
                            (
                              x
                            ) =>
                              x !==
                              p.id
                          )
                    )
                  }
                />

                <span>
                  <strong>
                    {p.code}
                  </strong>

                  <small>
                    {p.name}
                  </small>
                </span>
              </label>
            )
          )}
        </div>
      </div>

      {role?.code !==
        "ADMIN" && (
        <button
          className="primary-btn"
          onClick={save}
        >
          Save Permissions
        </button>
      )}

      {message && (
        <div
          className={
            message.includes(
              "success"
            )
              ? "success-note"
              : "form-error"
          }
        >
          {message}
        </div>
      )}
    </div>
  );
}

export function SystemConfigurationPage() {
  const [
    data,
    setData
  ] =
    useState<any>(
      null
    );

  useEffect(() => {
    api
      .get(
        "/admin/system"
      )
      .then((r) =>
        setData(
          r.data
        )
      );
  }, []);

  return (
    <div className="page-card">
      <div className="page-card-head">
        <div>
          <h2>
            System Configuration
          </h2>

          <p>
            Safe runtime configuration status. Secrets are never exposed here.
          </p>
        </div>
      </div>

      {data && (
        <div className="system-config-grid">
          <div>
            <small>
              Environment
            </small>

            <strong>
              {
                data.environment
              }
            </strong>
          </div>

          <div>
            <small>
              Oracle Read Connection
            </small>

            <strong>
              {data.oracleConfigured
                ? "Configured"
                : "Missing"}
            </strong>
          </div>

          <div>
            <small>
              Oracle Invoice Posting
            </small>

            <strong>
              {data.oracleInvoicePostConfigured
                ? "Configured"
                : "Pending contract"}
            </strong>
          </div>

          <div>
            <small>
              OTP Email
            </small>

            <strong>
              {data.smtpEnabled
                ? "SMTP enabled"
                : "Development mode"}
            </strong>
          </div>

          <div>
            <small>
              Max Upload
            </small>

            <strong>
              {
                data.maxUploadSizeMb
              }{" "}
              MB
            </strong>
          </div>

          <div>
            <small>
              Retention
            </small>

            <strong>
              {data.dataRetentionDays ===
              "0"
                ? "Awaiting PCL policy"
                : `${data.dataRetentionDays} days`}
            </strong>
          </div>
        </div>
      )}

      <div className="success-note">
        Production values are controlled through the backend environment configuration so credentials and secrets are not editable in the browser.
      </div>
    </div>
  );
}