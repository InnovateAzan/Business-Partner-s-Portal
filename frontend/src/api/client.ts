import axios from "axios";

// ============================================================
// API BASE URL
// ============================================================
//
// Same frontend build local aur live dono environments mein kaam karega.
//
// Example:
//
// Local:
// http://localhost:8088
// API => http://localhost:8099/api/v1
//
// Live:
// http://10.4.3.79:8088
// API => http://10.4.3.79:8099/api/v1
//
// ============================================================

const backendPort =
  import.meta.env.VITE_BACKEND_PORT ||
  "8099";

const apiHost =
  window.location.hostname;

const apiUrl =
  `http://${apiHost}:${backendPort}`;

const apiBaseUrl =
  `${apiUrl.replace(/\/+$/, "")}/api/v1`;

const sessionKey =
  import.meta.env.VITE_SESSION_STORAGE_KEY ||
  "bpp_auth";

// ============================================================
// AXIOS INSTANCE
// ============================================================

export const api =
  axios.create({
    baseURL: apiBaseUrl,
    timeout: 30000,
    withCredentials: true,
    headers: {
      "Content-Type":
        "application/json",
    },
  });

// ============================================================
// REQUEST INTERCEPTOR
// JWT TOKEN
// ============================================================

api.interceptors.request.use(
  (config) => {
    const raw =
      sessionStorage.getItem(
        sessionKey
      );

    if (raw) {
      try {
        const session =
          JSON.parse(raw) as {
            accessToken?: string;
          };

        if (
          session.accessToken
        ) {
          config.headers.Authorization =
            `Bearer ${session.accessToken}`;
        }
      } catch {
        sessionStorage.removeItem(
          sessionKey
        );
      }
    }

    return config;
  }
);

// ============================================================
// RESPONSE INTERCEPTOR
// ============================================================

api.interceptors.response.use(
  (response) =>
    response,

  (error) => {
    if (
      error?.response?.status ===
      401
    ) {
      sessionStorage.removeItem(
        sessionKey
      );

      if (
        !window.location.pathname.startsWith(
          "/login"
        )
      ) {
        window.location.assign(
          "/login"
        );
      }
    }

    return Promise.reject(
      error
    );
  }
);

// ============================================================
// ERROR MESSAGE HELPER
// ============================================================

export function getApiErrorMessage(
  error: unknown
): string {
  if (
    axios.isAxiosError(
      error
    )
  ) {
    return (
      error.response?.data
        ?.message ||
      error.response?.data
        ?.title ||
      error.message ||
      "Request failed."
    );
  }

  return error instanceof Error
    ? error.message
    : "Unexpected error.";
}