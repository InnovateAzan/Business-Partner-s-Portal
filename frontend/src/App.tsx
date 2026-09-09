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
  DownloadsPage,
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
  AdminDashboard,
} from "./pages/AdminDashboard";

import {
  IntegrationSupportPage,
} from "./pages/IntegrationSupportPage";

import {
  AuditPage,
  UserManagementPage,
  RolesPermissionsPage,
  SystemConfigurationPage,
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

            {/* =============================================
                VENDOR ROUTES
            ============================================= */}

            <Route
              path="/vendor"
              element={
                <VendorDashboard />
              }
            />

            <Route
              path="/purchase-orders"
              element={
                <PurchaseOrdersPage />
              }
            />

            <Route
              path="/grns"
              element={
                <GrnsPage />
              }
            />

            <Route
              path="/invoices/new"
              element={
                <SubmitInvoicePage />
              }
            />

            <Route
              path="/invoices/:id/resubmit"
              element={
                <SubmitInvoicePage />
              }
            />

            <Route
              path="/invoices"
              element={
                <RequestHistoryPage />
              }
            />

            <Route
              path="/payments"
              element={
                <PaymentsPage />
              }
            />

            <Route
              path="/vendor-profile"
              element={
                <VendorProfilePage />
              }
            />

            <Route
              path="/downloads"
              element={
                <DownloadsPage />
              }
            />

            <Route
              path="/support"
              element={
                <SupportPage />
              }
            />

            <Route
              path="/notifications"
              element={
                <NotificationsPage />
              }
            />

            {/* =============================================
                INTERNAL ROUTES
            ============================================= */}

            <Route
              path="/internal"
              element={
                <InternalDashboard />
              }
            />

            <Route
              path="/supply-chain/vendors"
              element={
                <VendorAccessPage />
              }
            />

            <Route
              path="/integration"
              element={
                <IntegrationSupportPage />
              }
            />

            {/* =============================================
                ADMIN ROUTES
            ============================================= */}

            <Route
              path="/admin"
              element={
                <AdminDashboard />
              }
            />

            {/* NEW COMBINED USERS & ROLES PAGE */}
            <Route
              path="/admin/users-roles"
              element={
                <UsersRolesPage />
              }
            />

            {/* Existing routes retained for compatibility */}
            <Route
              path="/admin/users"
              element={
                <UserManagementPage />
              }
            />

            <Route
              path="/admin/roles"
              element={
                <RolesPermissionsPage />
              }
            />

            <Route
              path="/admin/vendors"
              element={
                <VendorAccessPage />
              }
            />

            <Route
              path="/admin/audit"
              element={
                <AuditPage />
              }
            />

            <Route
              path="/admin/system"
              element={
                <SystemConfigurationPage />
              }
            />
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
