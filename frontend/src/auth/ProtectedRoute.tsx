import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";
import type { UserType } from "../types";

type ProtectedRouteProps = {
  allowed?: UserType[];
  requiredPermission?: string;
  requiredAnyPermission?: string[];
};

export function ProtectedRoute({
  allowed,
  requiredPermission,
  requiredAnyPermission,
}: ProtectedRouteProps) {
  const { user, hasPermission } = useAuth();

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  if (allowed && !allowed.includes(user.userType)) {
    return <Navigate to="/" replace />;
  }

  if (requiredPermission && !hasPermission(requiredPermission)) {
    return <Navigate to="/" replace />;
  }

  if (
    requiredAnyPermission &&
    requiredAnyPermission.length > 0 &&
    !requiredAnyPermission.some((permission) => hasPermission(permission))
  ) {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
}
