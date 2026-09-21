import {
  Navigate,
  Route,
  Routes,
} from "react-router-dom";

import {
  ProtectedRoute,
} from "./auth/ProtectedRoute";

import {
  useAuth,
} from "./auth/AuthContext";

import {
  IdleSessionManager,
} from "./auth/IdleSessionManager";

import {
  PortalLayout,
} from "./layouts/PortalLayout";

import {
  LoginPage,
} from "./pages/LoginPage";

import {
  SignupPage,
} from "./pages/SignupPage";

import {
  SetPasswordPage,
} from "./pages/SetPasswordPage";
import { ForgotPasswordPage, ResetPasswordPage, VerifyDevicePage } from "./pages/DeviceAndResetPages";

import {
  VendorDashboard,
} from "./pages/VendorDashboard";

import {
  PurchaseOrdersPage,
} from "./pages/PurchaseOrdersPage";

import {
  GrnsPage,
} from "./pages/GrnsPage";

import {
  PaymentsPage,
} from "./pages/PaymentsPage";

import {
  VendorProfilePage,
} from "./pages/VendorProfilePage";

import {
  SupportPage,
} from "./pages/SimplePages";

import {
  SubmitInvoicePage,
} from "./pages/SubmitInvoicePage";

import {
  RequestHistoryPage,
} from "./pages/RequestHistoryPage";

import {
  InternalDashboard,
} from "./pages/InternalDashboard";

import {
  FinanceInvoiceRecordsPage,
} from "./pages/FinanceInvoiceRecordsPage";

import {
  AdminDashboard,
} from "./pages/AdminDashboard";

import {
  IntegrationSupportPage,
} from "./pages/IntegrationSupportPage";

import {
  AuditPage,
  UserManagementPage,
  RolesPermissionsPage,
} from "./pages/AdminUtilityPages";

import {
  UsersRolesPage,
} from "./pages/UsersRolesPage";

import {
  VendorAccessPage,
} from "./pages/VendorAccessPage";

import {
  NotificationsPage,
} from "./pages/NotificationsPage";

function Home() {
  const {
    user,
  } =
    useAuth();

  if (!user) {
    return (
      <Navigate
        to="/login"
        replace
      />
    );
  }

  if (
    user.userType ===
    "VENDOR"
  ) {
    return (
      <Navigate
        to="/vendor"
        replace
      />
    );
  }

  if (
    user.userType ===
    "ADMIN"
  ) {
    return (
      <Navigate
        to="/admin"
        replace
      />
    );
  }

  return (
    <Navigate
      to="/internal"
      replace
    />
  );
}

export default function App() {
  return (
    <>
      <IdleSessionManager />

      <Routes>
        {/* =============================================
            PUBLIC ROUTES
        ============================================= */}

        <Route
          path="/login"
          element={
            <LoginPage />
          }
        />

        <Route
          path="/signup"
          element={
            <SignupPage />
          }
        />

        <Route
          path="/set-password"
          element={
            <SetPasswordPage />
          }
        />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/verify-device" element={<VerifyDevicePage />} />

        {/* =============================================
            PROTECTED ROUTES
        ============================================= */}

        <Route
          element={
            <ProtectedRoute />
          }
        >
          <Route
            element={
              <PortalLayout />
            }
          >
            <Route
              path="/"
              element={
                <Home />
              }
            />

            {/* VENDOR ROUTES */}
            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="DASHBOARD.VIEW" />}>
              <Route path="/vendor" element={<VendorDashboard />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="PO.VIEW" />}>
              <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="GRN.VIEW" />}>
              <Route path="/grns" element={<GrnsPage />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="INVOICE.CREATE" />}>
              <Route path="/invoices/new" element={<SubmitInvoicePage />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="INVOICE.RESUBMIT" />}>
              <Route path="/invoices/:id/resubmit" element={<SubmitInvoicePage />} />
            </Route>

            <Route
              element={
                <ProtectedRoute
                  allowed={["VENDOR"]}
                  requiredAnyPermission={["INVOICE.VIEW_OWN", "INVOICE.VIEW_ALL"]}
                />
              }
            >
              <Route path="/invoices" element={<RequestHistoryPage />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} requiredPermission="PAYMENT.VIEW" />}>
              <Route path="/payments" element={<PaymentsPage />} />
            </Route>

            <Route element={<ProtectedRoute allowed={["VENDOR"]} />}>
              <Route path="/vendor-profile" element={<VendorProfilePage />} />
            </Route>

            <Route path="/support" element={<SupportPage />} />
            <Route path="/notifications" element={<NotificationsPage />} />

            {/* INTERNAL ROUTES */}
            <Route
              element={
                <ProtectedRoute
                  allowed={["INTERNAL"]}
                  requiredPermission="DASHBOARD.VIEW"
                />
              }
            >
              <Route path="/internal" element={<InternalDashboard />} />
            </Route>

            <Route
              element={
                <ProtectedRoute
                  allowed={["INTERNAL"]}
                  requiredPermission="INVOICE.VIEW_ALL"
                />
              }
            >
              <Route path="/finance/invoices" element={<FinanceInvoiceRecordsPage />} />
            </Route>

            <Route
              element={
                <ProtectedRoute
                  allowed={["INTERNAL"]}
                  requiredPermission="VENDOR.MANAGE"
                />
              }
            >
              <Route path="/supply-chain/vendors" element={<VendorAccessPage />} />
            </Route>

            <Route
              element={
                <ProtectedRoute
                  allowed={["INTERNAL", "ADMIN"]}
                  requiredPermission="INTEGRATION.VIEW"
                />
              }
            >
              <Route path="/integration" element={<IntegrationSupportPage />} />
            </Route>

            {/* ADMIN ROUTES */}
            <Route element={<ProtectedRoute allowed={["ADMIN"]} />}>
              <Route path="/admin" element={<AdminDashboard />} />
              <Route path="/admin/users-roles" element={<UsersRolesPage />} />
              <Route path="/admin/users" element={<UserManagementPage />} />
              <Route path="/admin/roles" element={<RolesPermissionsPage />} />
              <Route path="/admin/vendors" element={<VendorAccessPage />} />
              <Route path="/admin/audit" element={<AuditPage />} />
            </Route>
          </Route>
        </Route>

        {/* =============================================
            FALLBACK
        ============================================= */}

        <Route
          path="*"
          element={
            <Navigate
              to="/"
              replace
            />
          }
        />
      </Routes>
    </>
  );
}
