import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { login, type LoginOtpChallenge } from "../api/auth";
import { getApiErrorMessage } from "../api/client";
import { useAuth } from "../auth/AuthContext";

const LAST_ACTIVITY_KEY = "bpp-last-user-activity";

export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const [showPassword, setShowPassword] =
    useState(false);

  const [error, setError] =
    useState("");

  const [loading, setLoading] =
    useState(false);

  async function handleSubmit(
    event: React.FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setError("");
    setLoading(true);

    try {
      const session = await login(
        email.trim(),
        password
      );

      if ("otpRequired" in session) {
        const challenge = session as LoginOtpChallenge;
        navigate(`/verify-device?challengeId=${encodeURIComponent(challenge.challengeId)}&email=${encodeURIComponent(challenge.maskedEmail)}`);
        return;
      }

      /*
       * IMPORTANT:
       * Successful login ke time idle-session timestamp reset.
       *
       * Isse previous session ka old idle timestamp
       * new login ko immediately logout nahi karega.
       */
      localStorage.setItem(
        LAST_ACTIVITY_KEY,
        String(Date.now())
      );

      setSession(session);

      const userType =
        session.user.userType;

      if (userType === "VENDOR") {
        navigate(
          "/vendor",
          { replace: true }
        );

        return;
      }

      if (userType === "ADMIN") {
        navigate(
          "/admin",
          { replace: true }
        );

        return;
      }

      navigate(
        "/internal",
        { replace: true }
      );
    } catch (err) {
      setError(
        getApiErrorMessage(err)
      );
    } finally {
      setLoading(false);
    }
  }

  function handleForgotPassword() { navigate("/forgot-password"); }

  return (
    <main className="login-page">
      <section className="login-card">
        {/* =====================================================
            LEFT BRAND PANEL
        ====================================================== */}

        <aside className="login-brand-panel">
          <div className="login-dot-pattern">
            {Array.from({ length: 9 }).map(
              (_, index) => (
                <span key={index} />
              )
            )}
          </div>

          <div className="login-brand-header">
            <img
              src="/pakistan-cables-logo.png"
              alt="Pakistan Cables"
              className="login-brand-logo"
            />

            <h2>
              PAKISTAN CABLES
            </h2>

            <p>
              Business Partner&apos;s Portal
            </p>

            <div className="login-brand-divider" />
          </div>

          <div className="login-welcome-copy">
            <span>
              Welcome back!
            </span>

            <h1>
              Business Partner&apos;s
              <br />
              Portal
            </h1>

            <p>
              Access purchase orders,
              GRNs, invoices and payment
              status securely.
            </p>
          </div>

          <div className="login-brand-bottom">
            <div className="login-artwork-wrap">
              <img
                src="/login-factory-art.png"
                alt=""
                className="login-factory-art"
                onError={(event) => {
                  event.currentTarget.style.display =
                    "none";
                }}
              />
            </div>

            <div className="login-company-copy">
              <strong>
                Pakistan Cables Limited
              </strong>

              <span>
                A trusted name since 1953
              </span>
            </div>
          </div>
        </aside>

        {/* =====================================================
            RIGHT LOGIN FORM
        ====================================================== */}

        <section className="login-form-panel">
          <form
            className="login-form"
            onSubmit={handleSubmit}
          >
            <div className="login-secure-badge">
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                aria-hidden="true"
              >
                <path
                  d="
                    M12 3
                    L19 6
                    V11
                    C19 15.4 16 19 12 21
                    C8 19 5 15.4 5 11
                    V6
                    L12 3
                    Z
                  "
                />

                <path d="M9 12L11 14L15 10" />
              </svg>

              <span>
                Secure Vendor Portal
              </span>
            </div>

            <div className="login-heading">
              <h1>
                Sign In
              </h1>

              <p>
                Continue to Business
                Partner&apos;s Portal
              </p>
            </div>

            {/* EMAIL */}

            <label className="login-field">
              <span className="login-field-label">
                Email Address
              </span>

              <div className="login-input">
                <span className="login-input-icon">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    aria-hidden="true"
                  >
                    <rect
                      x="3"
                      y="5"
                      width="18"
                      height="14"
                      rx="2"
                    />

                    <path d="M3 7L12 13L21 7" />
                  </svg>
                </span>

                <input
                  type="email"
                  value={email}
                  onChange={(event) =>
                    setEmail(
                      event.target.value
                    )
                  }
                  placeholder="Enter your email"
                  autoComplete="email"
                  required
                />
              </div>
            </label>

            {/* PASSWORD */}

            <label className="login-field">
              <span className="login-field-label">
                Password
              </span>

              <div className="login-input">
                <span className="login-input-icon">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    aria-hidden="true"
                  >
                    <rect
                      x="5"
                      y="10"
                      width="14"
                      height="10"
                      rx="2"
                    />

                    <path
                      d="
                        M8 10
                        V7
                        C8 4.8 9.8 3 12 3
                        C14.2 3 16 4.8 16 7
                        V10
                      "
                    />
                  </svg>
                </span>

                <input
                  type={
                    showPassword
                      ? "text"
                      : "password"
                  }
                  value={password}
                  onChange={(event) =>
                    setPassword(
                      event.target.value
                    )
                  }
                  placeholder="Enter your password"
                  autoComplete="current-password"
                  required
                />

                <button
                  type="button"
                  className="login-password-toggle"
                  onClick={() =>
                    setShowPassword(
                      (current) =>
                        !current
                    )
                  }
                  aria-label={
                    showPassword
                      ? "Hide password"
                      : "Show password"
                  }
                >
                  {showPassword ? (
                    <svg
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      aria-hidden="true"
                    >
                      <path d="M3 3L21 21" />

                      <path
                        d="
                          M10.6 10.6
                          A2 2 0 0 0
                          13.4 13.4
                        "
                      />

                      <path
                        d="
                          M9.9 4.2
                          A9.7 9.7 0 0 1
                          12 4
                          C17 4 20.3 8 22 12
                        "
                      />

                      <path
                        d="
                          M6.6 6.6
                          A13.8 13.8 0 0 0
                          2 12
                          C3.7 16 7 20 12 20
                          C13.1 20 14.1 19.8 15 19.4
                        "
                      />
                    </svg>
                  ) : (
                    <svg
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      aria-hidden="true"
                    >
                      <path
                        d="
                          M2 12
                          C4.5 7.5 8 5.5 12 5.5
                          C16 5.5 19.5 7.5 22 12
                          C19.5 16.5 16 18.5 12 18.5
                          C8 18.5 4.5 16.5 2 12
                          Z
                        "
                      />

                      <circle
                        cx="12"
                        cy="12"
                        r="3"
                      />
                    </svg>
                  )}
                </button>
              </div>
            </label>

            <div className="login-forgot-row">
              <button
                type="button"
                className="login-forgot-link"
                onClick={
                  handleForgotPassword
                }
              >
                Forgot Password?
              </button>
            </div>

            {error && (
              <div className="form-error login-error">
                {error}
              </div>
            )}

            <button
              type="submit"
              className="login-submit-btn"
              disabled={loading}
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                aria-hidden="true"
              >
                <rect
                  x="6"
                  y="10"
                  width="12"
                  height="10"
                  rx="2"
                />

                <path
                  d="
                    M9 10
                    V7
                    C9 5.3 10.3 4 12 4
                    C13.7 4 15 5.3 15 7
                    V10
                  "
                />
              </svg>

              {loading
                ? "Signing In..."
                : "Sign In"}
            </button>

            <div className="login-oracle-note">
              <span className="login-info-icon">
                i
              </span>

              <p>
                Vendor must already exist
                in Oracle EBS before
                registration.
              </p>
            </div>
          </form>
        </section>
      </section>
    </main>
  );
}
