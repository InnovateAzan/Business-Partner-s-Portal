import { createContext, useContext, useMemo, useState, type PropsWithChildren } from "react";
import type { LoginResponse, SessionUser } from "../types";

const sessionKey = import.meta.env.VITE_SESSION_STORAGE_KEY || "bpp_auth";

type StoredSession = LoginResponse;

interface AuthContextValue {
  user: SessionUser | null;
  accessToken: string | null;
  setSession: (session: StoredSession) => void;
  logout: () => void;
  hasPermission: (permission: string) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function readSession(): StoredSession | null {
  const raw = sessionStorage.getItem(sessionKey);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as StoredSession;
  } catch {
    sessionStorage.removeItem(sessionKey);
    return null;
  }
}

export function AuthProvider({ children }: PropsWithChildren) {
  const [session, updateSession] = useState<StoredSession | null>(() => readSession());

  const value = useMemo<AuthContextValue>(() => ({
    user: session?.user ?? null,
    accessToken: session?.accessToken ?? null,
    setSession: (next) => {
      sessionStorage.setItem(sessionKey, JSON.stringify(next));
      updateSession(next);
    },
    logout: () => {
      sessionStorage.removeItem(sessionKey);
      updateSession(null);
    },
    hasPermission: (permission) => {
      if (!session?.user) return false;
      if (session.user.userType === "ADMIN") return true;
      return session.user.permissions.includes(permission);
    }
  }), [session]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
