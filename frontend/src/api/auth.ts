import { api } from "./client";
import type { LoginResponse, SessionUser } from "../types";

export async function login(email: string, password: string): Promise<LoginResponse> {
  const { data } = await api.post<LoginResponse>("/auth/login", { email, password });
  return data;
}

export async function getMe(): Promise<SessionUser> {
  const { data } = await api.get<SessionUser>("/auth/me");
  return data;
}
