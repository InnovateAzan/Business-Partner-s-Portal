import axios from "axios";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || "https://localhost:7044/api/v1";
const sessionKey = import.meta.env.VITE_SESSION_STORAGE_KEY || "bpp_auth";

export const api = axios.create({
  baseURL: apiBaseUrl,
  timeout: 30000,
  headers: { "Content-Type": "application/json" }
});

api.interceptors.request.use((config) => {
  const raw = sessionStorage.getItem(sessionKey);
  if (raw) {
    try {
      const session = JSON.parse(raw) as { accessToken?: string };
      if (session.accessToken) {
        config.headers.Authorization = `Bearer ${session.accessToken}`;
      }
    } catch {
      sessionStorage.removeItem(sessionKey);
    }
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401) {
      sessionStorage.removeItem(sessionKey);
      if (!window.location.pathname.startsWith("/login")) {
        window.location.assign("/login");
      }
    }
    return Promise.reject(error);
  }
);

export function getApiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    return (
      error.response?.data?.message ||
      error.response?.data?.title ||
      error.message ||
      "Request failed."
    );
  }
  return error instanceof Error ? error.message : "Unexpected error.";
}
