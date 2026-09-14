import axios from "axios";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL;

if (!apiBaseUrl) {
  throw new Error(
    "VITE_API_BASE_URL is required. Set it to the LAN-reachable API URL, for example http://<SERVER-IP>:5044/api/v1."
  );
}
const sessionKey = import.meta.env.VITE_SESSION_STORAGE_KEY || "bpp_auth";

export const api = axios.create({
  baseURL: apiBaseUrl,
  timeout: 30000,
  withCredentials: true,
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
