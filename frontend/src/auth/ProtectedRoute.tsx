import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";
import type { UserType } from "../types";

export function ProtectedRoute({ allowed }: { allowed?: UserType[] }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (allowed && !allowed.includes(user.userType)) return <Navigate to="/" replace />;
  return <Outlet />;
}
