import {
  useEffect,
  useMemo,
  useState,
} from "react";

import { api, getApiErrorMessage } from "../api/client";

import "./UsersRolesPage.css";

type TabType = "USERS" | "ROLES";

type Role = {
  id: string;
  code: string;
  name: string;
};

type Permission = {
  id: string;
  code: string;
  name: string;
};

type PortalUser = {
  id: string;
  fullName: string;
  email: string;
  userType: string;
  isActive: boolean;
  isSuperAdmin?: boolean;
  lastLoginAt?: string | null;
  roles?: string[];
};

type UserForm = {
  id?: string;
  fullName: string;
  email: string;
  userType: string;
  roleCodes: string[];
  password: string;
  isActive: boolean;
  isSuperAdmin: boolean;
};

const emptyUserForm: UserForm = {
  fullName: "",
  email: "",
  userType: "INTERNAL",
  roleCodes: ["SUPPLY_CHAIN"],
  password: "",
  isActive: true,
  isSuperAdmin: false,
};

function getRoleDescription(code: string) {
  const normalized = code.toUpperCase();

  if (normalized === "ADMIN") {
    return "Full access including user, role and system administration.";
  }

  if (normalized === "FINANCE") {
    return "Finance and AP visibility for invoices, payments and related records.";
  }

  if (normalized === "SUPPLY_CHAIN") {
    return "Vendor onboarding, portal access and supply chain visibility.";
  }

  if (normalized === "INTEGRATION_SUPPORT") {
    return "Monitor Oracle integration status, failures and retries.";
  }

  if (normalized === "VENDOR") {
    return "Vendor self-service access to POs, GRNs, invoices and payments.";
  }

  return "Custom portal role with configurable permissions.";
}

function getPermissionGroup(code: string) {
  const prefix = code.split(".")[0]?.toUpperCase();

  switch (prefix) {
    case "DASHBOARD":
      return "Dashboard";

    case "PO":
      return "Purchase Orders";

    case "GRN":
      return "GRNs";

    case "INVOICE":
      return "Invoices";

    case "PAYMENT":
      return "Payments";

    case "DOCUMENT":
      return "Documents";

    case "VENDOR":
      return "Vendor Management";

    case "USER":
      return "User Management";

    case "ROLE":
      return "Role Management";

    case "INTEGRATION":
      return "Oracle Integration";

    case "AUDIT":
      return "Audit";

    case "SETTINGS":
      return "System Configuration";

    default:
      return "Other";
  }
}

function createRoleCode(name: string) {
  return name
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "");
}

export function UsersRolesPage() {
  const [activeTab, setActiveTab] =
    useState<TabType>("USERS");

  const [users, setUsers] =
    useState<PortalUser[]>([]);

  const [roles, setRoles] =
    useState<Role[]>([]);

  const [permissions, setPermissions] =
    useState<Permission[]>([]);

  const [rolePermissions, setRolePermissions] =
    useState<Record<string, string[]>>({});

  const [loading, setLoading] =
    useState(true);

  const [pageError, setPageError] =
    useState("");

  // ============================================================
  // USER MODAL
  // ============================================================

  const [userModalOpen, setUserModalOpen] =
    useState(false);

  const [userForm, setUserForm] =
    useState<UserForm>(emptyUserForm);

  const [userSaving, setUserSaving] =
    useState(false);

  const [userError, setUserError] =
    useState("");

  // ============================================================
  // ROLE MODAL
  // ============================================================

  const [roleModalOpen, setRoleModalOpen] =
    useState(false);

  const [roleName, setRoleName] =
    useState("");

  const [roleCode, setRoleCode] =
    useState("");

  const [roleSaving, setRoleSaving] =
    useState(false);

  const [roleError, setRoleError] =
    useState("");

  // ============================================================
  // PERMISSION MODAL
  // ============================================================

  const [
    permissionModalRole,
    setPermissionModalRole,
  ] = useState<Role | null>(null);

  const [
    selectedPermissions,
    setSelectedPermissions,
  ] = useState<string[]>([]);

  const [permissionSaving, setPermissionSaving] =
    useState(false);

  const [permissionMessage, setPermissionMessage] =
    useState("");

  const [permissionError, setPermissionError] =
    useState("");

  // ============================================================
  // LOAD DATA
  // ============================================================

  async function loadData() {
    setLoading(true);
    setPageError("");

    try {
      const [
        usersResponse,
        rolesResponse,
        permissionsResponse,
      ] = await Promise.all([
        api.get("/admin/users"),
        api.get("/admin/roles"),
        api.get("/admin/permissions"),
      ]);

      const loadedUsers: PortalUser[] =
        usersResponse.data ?? [];

      const loadedRoles: Role[] =
        rolesResponse.data ?? [];

      const loadedPermissions: Permission[] =
        permissionsResponse.data ?? [];

      setUsers(loadedUsers);
      setRoles(loadedRoles);
      setPermissions(loadedPermissions);

      const permissionEntries =
        await Promise.all(
          loadedRoles.map(async (role) => {
            if (
              role.code.toUpperCase() ===
              "ADMIN"
            ) {
              return [
                role.id,
                loadedPermissions.map(
                  (permission) =>
                    permission.id
                ),
              ] as const;
            }

            try {
              const response =
                await api.get(
                  `/admin/roles/${role.id}/permissions`
                );

              return [
                role.id,
                response.data ?? [],
              ] as const;
            } catch {
              return [
                role.id,
                [],
              ] as const;
            }
          })
        );

      setRolePermissions(
        Object.fromEntries(
          permissionEntries
        )
      );
    } catch (error) {
      setPageError(
        getApiErrorMessage(error)
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadData();
  }, []);

  // ============================================================
  // COMPUTED DATA
  // ============================================================

  const permissionGroups =
    useMemo(() => {
      const groups: Record<
        string,
        Permission[]
      > = {};

      permissions.forEach(
        (permission) => {
          const group =
            getPermissionGroup(
              permission.code
            );

          if (!groups[group]) {
            groups[group] = [];
          }

          groups[group].push(
            permission
          );
        }
      );

      return groups;
    }, [permissions]);

  function countUsersForRole(
    roleCode: string
  ) {
    return users.filter(
      (user) =>
        user.roles?.some(
          (code) =>
            code.toUpperCase() ===
            roleCode.toUpperCase()
        )
    ).length;
  }

  function permissionCount(
    role: Role
  ) {
    if (
      role.code.toUpperCase() ===
      "ADMIN"
    ) {
      return permissions.length;
    }

    return (
      rolePermissions[role.id]
        ?.length ?? 0
    );
  }

  // ============================================================
  // ADD / EDIT USER
  // ============================================================

  function openAddUser() {
    setUserError("");

    setUserForm({
      ...emptyUserForm,
      roleCodes:
        roles.length > 0
          ? [
              roles.find(
                (role) =>
                  role.code ===
                  "SUPPLY_CHAIN"
              )?.code ??
                roles[0].code,
            ]
          : [],
    });

    setUserModalOpen(true);
  }

  function openEditUser(
    user: PortalUser
  ) {
    setUserError("");

    setUserForm({
      id: user.id,
      fullName: user.fullName,
      email: user.email,
      userType: user.userType,
      roleCodes:
        user.roles ?? [],
      password: "",
      isActive: user.isActive,
      isSuperAdmin:
        user.isSuperAdmin ?? false,
    });

    setUserModalOpen(true);
  }

  function toggleUserRole(
    roleCode: string
  ) {
    setUserForm(
      (current) => {
        const exists =
          current.roleCodes.includes(
            roleCode
          );

        return {
          ...current,

          roleCodes: exists
            ? current.roleCodes.filter(
                (code) =>
                  code !== roleCode
              )
            : [
                ...current.roleCodes,
                roleCode,
              ],
        };
      }
    );
  }

  async function saveUser(
    event: React.FormEvent
  ) {
    event.preventDefault();

    setUserError("");
    setUserSaving(true);

    try {
      if (
        !userForm.fullName.trim()
      ) {
        throw new Error(
          "Full name is required."
        );
      }

      if (!userForm.email.trim()) {
        throw new Error(
          "Email is required."
        );
      }

      if (
        !userForm.id &&
        userForm.password.length < 10
      ) {
        throw new Error(
          "Temporary password must be at least 10 characters."
        );
      }

      if (
        userForm.roleCodes.length === 0
      ) {
        throw new Error(
          "Select at least one role."
        );
      }

      const payload = {
        fullName:
          userForm.fullName.trim(),

        email:
          userForm.email.trim(),

        userType:
          userForm.userType,

        roleCodes:
          userForm.roleCodes,

        password:
          userForm.password,

        isActive:
          userForm.isActive,

        isSuperAdmin:
          userForm.isSuperAdmin,
      };

      if (userForm.id) {
        await api.put(
          `/admin/users/${userForm.id}`,
          payload
        );
      } else {
        await api.post(
          "/admin/users",
          payload
        );
      }

      setUserModalOpen(false);

      await loadData();
    } catch (error) {
      setUserError(
        getApiErrorMessage(error)
      );
    } finally {
      setUserSaving(false);
    }
  }

  // ============================================================
  // ADD ROLE
  // ============================================================

  function openAddRole() {
    setRoleName("");
    setRoleCode("");
    setRoleError("");
    setRoleModalOpen(true);
  }

  async function saveRole(
    event: React.FormEvent
  ) {
    event.preventDefault();

    setRoleError("");
    setRoleSaving(true);

    try {
      const finalName =
        roleName.trim();

      const finalCode =
        (
          roleCode.trim() ||
          createRoleCode(finalName)
        ).toUpperCase();

      if (!finalName) {
        throw new Error(
          "Role name is required."
        );
      }

      if (!finalCode) {
        throw new Error(
          "Role code is required."
        );
      }

      await api.post(
        "/admin/roles",
        {
          name: finalName,
          code: finalCode,
        }
      );

      setRoleModalOpen(false);

      await loadData();
    } catch (error) {
      setRoleError(
        getApiErrorMessage(error)
      );
    } finally {
      setRoleSaving(false);
    }
  }

  // ============================================================
  // PERMISSIONS
  // ============================================================

  async function openPermissions(
    role: Role
  ) {
    setPermissionMessage("");
    setPermissionError("");

    setPermissionModalRole(
      role
    );

    if (
      role.code.toUpperCase() ===
      "ADMIN"
    ) {
      setSelectedPermissions(
        permissions.map(
          (permission) =>
            permission.id
        )
      );

      return;
    }

    try {
      const response =
        await api.get(
          `/admin/roles/${role.id}/permissions`
        );

      setSelectedPermissions(
        response.data ?? []
      );
    } catch (error) {
      setPermissionError(
        getApiErrorMessage(error)
      );

      setSelectedPermissions([]);
    }
  }

  function togglePermission(
    permissionId: string
  ) {
    if (
      permissionModalRole?.code.toUpperCase() ===
      "ADMIN"
    ) {
      return;
    }

    setSelectedPermissions(
      (current) =>
        current.includes(
          permissionId
        )
          ? current.filter(
              (id) =>
                id !== permissionId
            )
          : [
              ...current,
              permissionId,
            ]
    );
  }

  function setPermissionGroup(
    groupPermissions: Permission[],
    checked: boolean
  ) {
    if (
      permissionModalRole?.code.toUpperCase() ===
      "ADMIN"
    ) {
      return;
    }

    const ids =
      groupPermissions.map(
        (permission) =>
          permission.id
      );

    setSelectedPermissions(
      (current) => {
        if (checked) {
          return Array.from(
            new Set([
              ...current,
              ...ids,
            ])
          );
        }

        return current.filter(
          (id) =>
            !ids.includes(id)
        );
      }
    );
  }

  async function savePermissions() {
    if (!permissionModalRole) {
      return;
    }

    if (
      permissionModalRole.code.toUpperCase() ===
      "ADMIN"
    ) {
      return;
    }

    setPermissionSaving(true);
    setPermissionError("");
    setPermissionMessage("");

    try {
      await api.put(
        `/admin/roles/${permissionModalRole.id}/permissions`,
        {
          permissionIds:
            selectedPermissions,
        }
      );

      setRolePermissions(
        (current) => ({
          ...current,

          [permissionModalRole.id]:
            selectedPermissions,
        })
      );

      setPermissionMessage(
        "Permissions saved successfully."
      );
    } catch (error) {
      setPermissionError(
        getApiErrorMessage(error)
      );
    } finally {
      setPermissionSaving(false);
    }
  }

  return (
    <div className="page-card users-roles-page">
      {/* ========================================================
          HEADER
      ======================================================== */}

      <div className="users-roles-header">
        <div>
          <h2>
            Users &amp; Roles
          </h2>

          <p>
            Control who can access
            the portal and what they
            can do.
          </p>
        </div>
      </div>

      {/* ========================================================
          TABS
      ======================================================== */}

      <div className="users-roles-tabs">
        <button
          type="button"
          className={
            activeTab === "USERS"
              ? "active"
              : ""
          }
          onClick={() =>
            setActiveTab("USERS")
          }
        >
          Users
        </button>

        <button
          type="button"
          className={
            activeTab === "ROLES"
              ? "active"
              : ""
          }
          onClick={() =>
            setActiveTab("ROLES")
          }
        >
          Roles
        </button>
      </div>

      {pageError && (
        <div className="form-error">
          {pageError}
        </div>
      )}

      {loading ? (
        <div className="users-roles-loading">
          Loading users and roles...
        </div>
      ) : activeTab === "USERS" ? (
        <>
          {/* ====================================================
              USERS TAB
          ==================================================== */}

          <div className="users-roles-toolbar">
            <div>
              <strong>
                {users.length}
              </strong>{" "}
              {users.length === 1
                ? "user"
                : "users"}
            </div>

            <button
              type="button"
              className="primary-btn"
              onClick={openAddUser}
            >
              + Add User
            </button>
          </div>

          <div className="users-roles-table-wrap">
            <table className="data-table users-roles-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Email</th>
                  <th>Type</th>
                  <th>Roles</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>

              <tbody>
                {users.map(
                  (user) => (
                    <tr
                      key={user.id}
                    >
                      <td>
                        <strong>
                          {user.fullName}
                        </strong>
                      </td>

                      <td>
                        {user.email}
                      </td>

                      <td>
                        {user.userType}
                      </td>

                      <td>
                        <div className="role-chip-list">
                          {user.roles
                            ?.length ? (
                            user.roles.map(
                              (role) => (
                                <span
                                  key={
                                    role
                                  }
                                  className="role-chip"
                                >
                                  {role}
                                </span>
                              )
                            )
                          ) : (
                            "-"
                          )}
                        </div>
                      </td>

                      <td>
                        <span
                          className={`status ${
                            user.isActive
                              ? "green"
                              : "red"
                          }`}
                        >
                          {user.isActive
                            ? "Active"
                            : "Disabled"}
                        </span>
                      </td>

                      <td>
                        <button
                          type="button"
                          className="table-btn"
                          onClick={() =>
                            openEditUser(
                              user
                            )
                          }
                        >
                          Edit
                        </button>
                      </td>
                    </tr>
                  )
                )}

                {users.length ===
                  0 && (
                  <tr>
                    <td
                      colSpan={6}
                      className="empty"
                    >
                      No users found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      ) : (
        <>
          {/* ====================================================
              ROLES TAB
          ==================================================== */}

          <div className="users-roles-toolbar">
            <div>
              <strong>
                {roles.length}
              </strong>{" "}
              {roles.length === 1
                ? "role"
                : "roles"}
            </div>

            <button
              type="button"
              className="primary-btn"
              onClick={openAddRole}
            >
              + Add Role
            </button>
          </div>

          <div className="users-roles-table-wrap">
            <table className="data-table users-roles-table roles-table">
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Description</th>
                  <th>Users</th>
                  <th>
                    Permissions
                  </th>
                  <th>Action</th>
                </tr>
              </thead>

              <tbody>
                {roles.map(
                  (role) => (
                    <tr
                      key={role.id}
                    >
                      <td>
                        <div className="role-name-cell">
                          <strong>
                            {role.name}
                          </strong>

                          {role.code ===
                            "ADMIN" && (
                            <span className="built-in-badge">
                              🔒 Built-in
                            </span>
                          )}
                        </div>

                        <small>
                          {role.code}
                        </small>
                      </td>

                      <td className="role-description">
                        {getRoleDescription(
                          role.code
                        )}
                      </td>

                      <td>
                        {countUsersForRole(
                          role.code
                        )}
                      </td>

                      <td>
                        <button
                          type="button"
                          className="permission-count-btn"
                          onClick={() =>
                            openPermissions(
                              role
                            )
                          }
                        >
                          {permissionCount(
                            role
                          )}{" "}
                          granted
                        </button>
                      </td>

                      <td>
                        <button
                          type="button"
                          className="table-btn"
                          onClick={() =>
                            openPermissions(
                              role
                            )
                          }
                        >
                          Permissions
                        </button>
                      </td>
                    </tr>
                  )
                )}

                {roles.length ===
                  0 && (
                  <tr>
                    <td
                      colSpan={5}
                      className="empty"
                    >
                      No roles found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}

      {/* ========================================================
          ADD / EDIT USER MODAL
      ======================================================== */}

      {userModalOpen && (
        <div
          className="admin-modal-backdrop"
          onMouseDown={() =>
            setUserModalOpen(
              false
            )
          }
        >
          <div
            className="admin-modal user-modal"
            onMouseDown={(
              event
            ) =>
              event.stopPropagation()
            }
          >
            <div className="admin-modal-header">
              <div>
                <h3>
                  {userForm.id
                    ? "Edit User"
                    : "Add User"}
                </h3>

                <p>
                  Configure portal
                  identity and role
                  access.
                </p>
              </div>

              <button
                type="button"
                className="admin-modal-close"
                onClick={() =>
                  setUserModalOpen(
                    false
                  )
                }
              >
                ×
              </button>
            </div>

            <form
              onSubmit={saveUser}
              className="admin-modal-body"
            >
              <div className="admin-form-grid">
                <label>
                  Full Name

                  <input
                    value={
                      userForm.fullName
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        fullName:
                          event.target
                            .value,
                      })
                    }
                    required
                  />
                </label>

                <label>
                  Email

                  <input
                    type="email"
                    value={
                      userForm.email
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        email:
                          event.target
                            .value,
                      })
                    }
                    required
                  />
                </label>

                <label>
                  User Type

                  <select
                    value={
                      userForm.userType
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        userType:
                          event.target
                            .value,
                      })
                    }
                  >
                    <option value="INTERNAL">
                      INTERNAL
                    </option>

                    <option value="ADMIN">
                      ADMIN
                    </option>

                    <option value="VENDOR">
                      VENDOR
                    </option>
                  </select>
                </label>

                <label>
                  {userForm.id
                    ? "New Password (Optional)"
                    : "Temporary Password"}

                  <input
                    type="password"
                    value={
                      userForm.password
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        password:
                          event.target
                            .value,
                      })
                    }
                    required={
                      !userForm.id
                    }
                  />
                </label>
              </div>

              <div className="admin-form-section">
                <h4>
                  Roles
                </h4>

                <div className="role-selection-grid">
                  {roles.map(
                    (role) => (
                      <label
                        key={
                          role.id
                        }
                        className="role-selection"
                      >
                        <input
                          type="checkbox"
                          checked={userForm.roleCodes.includes(
                            role.code
                          )}
                          onChange={() =>
                            toggleUserRole(
                              role.code
                            )
                          }
                        />

                        <span>
                          <strong>
                            {role.name}
                          </strong>

                          <small>
                            {
                              role.code
                            }
                          </small>
                        </span>
                      </label>
                    )
                  )}
                </div>
              </div>

              <div className="user-status-options">
                <label>
                  <input
                    type="checkbox"
                    checked={
                      userForm.isActive
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        isActive:
                          event.target
                            .checked,
                      })
                    }
                  />

                  Active User
                </label>

                <label>
                  <input
                    type="checkbox"
                    checked={
                      userForm.isSuperAdmin
                    }
                    onChange={(
                      event
                    ) =>
                      setUserForm({
                        ...userForm,
                        isSuperAdmin:
                          event.target
                            .checked,
                      })
                    }
                  />

                  Super Admin
                </label>
              </div>

              {userError && (
                <div className="form-error">
                  {userError}
                </div>
              )}

              <div className="admin-modal-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() =>
                    setUserModalOpen(
                      false
                    )
                  }
                >
                  Cancel
                </button>

                <button
                  type="submit"
                  className="primary-btn"
                  disabled={
                    userSaving
                  }
                >
                  {userSaving
                    ? "Saving..."
                    : userForm.id
                      ? "Save Changes"
                      : "Add User"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ========================================================
          ADD ROLE MODAL
      ======================================================== */}

      {roleModalOpen && (
        <div
          className="admin-modal-backdrop"
          onMouseDown={() =>
            setRoleModalOpen(
              false
            )
          }
        >
          <div
            className="admin-modal small-admin-modal"
            onMouseDown={(
              event
            ) =>
              event.stopPropagation()
            }
          >
            <div className="admin-modal-header">
              <div>
                <h3>
                  Add Role
                </h3>

                <p>
                  Create a new portal
                  role. Permissions can
                  be assigned afterward.
                </p>
              </div>

              <button
                type="button"
                className="admin-modal-close"
                onClick={() =>
                  setRoleModalOpen(
                    false
                  )
                }
              >
                ×
              </button>
            </div>

            <form
              onSubmit={saveRole}
              className="admin-modal-body"
            >
              <label>
                Role Name

                <input
                  value={roleName}
                  placeholder="e.g. Procurement"
                  onChange={(
                    event
                  ) => {
                    const value =
                      event.target
                        .value;

                    setRoleName(
                      value
                    );

                    setRoleCode(
                      createRoleCode(
                        value
                      )
                    );
                  }}
                  required
                />
              </label>

              <label>
                Role Code

                <input
                  value={roleCode}
                  placeholder="PROCUREMENT"
                  onChange={(
                    event
                  ) =>
                    setRoleCode(
                      event.target
                        .value
                        .toUpperCase()
                    )
                  }
                  required
                />
              </label>

              {roleError && (
                <div className="form-error">
                  {roleError}
                </div>
              )}

              <div className="admin-modal-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() =>
                    setRoleModalOpen(
                      false
                    )
                  }
                >
                  Cancel
                </button>

                <button
                  type="submit"
                  className="primary-btn"
                  disabled={
                    roleSaving
                  }
                >
                  {roleSaving
                    ? "Creating..."
                    : "Add Role"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ========================================================
          PERMISSIONS MODAL
      ======================================================== */}

      {permissionModalRole && (
        <div
          className="admin-modal-backdrop"
          onMouseDown={() =>
            setPermissionModalRole(
              null
            )
          }
        >
          <div
            className="admin-modal permission-modal"
            onMouseDown={(
              event
            ) =>
              event.stopPropagation()
            }
          >
            <div className="admin-modal-header">
              <div>
                <h3>
                  {
                    permissionModalRole.name
                  }{" "}
                  Permissions
                </h3>

                <p>
                  Configure what this
                  role can access and
                  manage.
                </p>
              </div>

              <button
                type="button"
                className="admin-modal-close"
                onClick={() =>
                  setPermissionModalRole(
                    null
                  )
                }
              >
                ×
              </button>
            </div>

            <div className="permission-modal-scroll">
              {permissionModalRole.code.toUpperCase() ===
                "ADMIN" && (
                <div className="admin-permission-note">
                  Administrator has
                  unrestricted access.
                  Its permissions cannot
                  be reduced.
                </div>
              )}

              {Object.entries(
                permissionGroups
              ).map(
                ([
                  groupName,
                  groupPermissions,
                ]) => {
                  const allChecked =
                    groupPermissions.every(
                      (permission) =>
                        selectedPermissions.includes(
                          permission.id
                        )
                    );

                  return (
                    <section
                      className="permission-group"
                      key={
                        groupName
                      }
                    >
                      <div className="permission-group-header">
                        <h4>
                          {
                            groupName
                          }
                        </h4>

                        {permissionModalRole.code.toUpperCase() !==
                          "ADMIN" && (
                          <button
                            type="button"
                            onClick={() =>
                              setPermissionGroup(
                                groupPermissions,
                                !allChecked
                              )
                            }
                          >
                            {allChecked
                              ? "Clear all"
                              : "Select all"}
                          </button>
                        )}
                      </div>

                      <div className="permission-modal-grid">
                        {groupPermissions.map(
                          (
                            permission
                          ) => (
                            <label
                              key={
                                permission.id
                              }
                              className="modal-permission-check"
                            >
                              <input
                                type="checkbox"
                                disabled={
                                  permissionModalRole.code.toUpperCase() ===
                                  "ADMIN"
                                }
                                checked={selectedPermissions.includes(
                                  permission.id
                                )}
                                onChange={() =>
                                  togglePermission(
                                    permission.id
                                  )
                                }
                              />

                              <span>
                                <strong>
                                  {
                                    permission.code
                                  }
                                </strong>

                                <small>
                                  {
                                    permission.name
                                  }
                                </small>
                              </span>
                            </label>
                          )
                        )}
                      </div>
                    </section>
                  );
                }
              )}
            </div>

            {permissionMessage && (
              <div className="success-note permission-feedback">
                {
                  permissionMessage
                }
              </div>
            )}

            {permissionError && (
              <div className="form-error permission-feedback">
                {
                  permissionError
                }
              </div>
            )}

            <div className="admin-modal-actions permission-modal-actions">
              <button
                type="button"
                className="secondary-btn"
                onClick={() =>
                  setPermissionModalRole(
                    null
                  )
                }
              >
                Close
              </button>

              {permissionModalRole.code.toUpperCase() !==
                "ADMIN" && (
                <button
                  type="button"
                  className="primary-btn"
                  disabled={
                    permissionSaving
                  }
                  onClick={
                    savePermissions
                  }
                >
                  {permissionSaving
                    ? "Saving..."
                    : "Save Permissions"}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}