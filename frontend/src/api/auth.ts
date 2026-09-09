import { api } from "./client";
import type { LoginResponse, SessionUser } from "../types";

export async function login(email: string, password: string): Promise<LoginResponse | LoginOtpChallenge> {
  const { data } = await api.post<LoginResponse | LoginOtpChallenge>("/auth/login", { email, password });
  return data;
}
export interface LoginOtpChallenge { otpRequired: true; challengeId: string; maskedEmail: string; }
export async function requestPasswordReset(email: string) { await api.post("/auth/forgot-password", { email }); }
export async function resetPassword(token: string, password: string, confirmPassword: string) { await api.post("/auth/reset-password", { token, password, confirmPassword }); }
export async function verifyLoginOtp(challengeId: string, otp: string): Promise<LoginResponse> { const { data } = await api.post<LoginResponse>("/auth/verify-login-otp", { challengeId, otp }); return data; }
export async function resendLoginOtp(challengeId: string): Promise<{ challengeId: string }> { const { data } = await api.post<{ challengeId: string }>("/auth/resend-login-otp", { challengeId, otp: "" }); return data; }

export async function getMe(): Promise<SessionUser> {
  const { data } = await api.get<SessionUser>("/auth/me");
  return data;
}
