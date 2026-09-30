import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { requestPasswordReset, resetPassword, resendLoginOtp, verifyLoginOtp } from "../api/auth";
import { api, getApiErrorMessage } from "../api/client";
import { useAuth } from "../auth/AuthContext";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState(""); const [sent, setSent] = useState(false); const [error, setError] = useState("");
  async function submit(e: React.FormEvent) { e.preventDefault(); setError(""); try { await requestPasswordReset(email); setSent(true); } catch (e) { setError(getApiErrorMessage(e)); } }
  return <main className="auth-page"><section className="signup-card"><h1>Forgot Password</h1>{sent ? <p className="success-note">If an account exists for that email address, a password reset link has been sent.</p> : <form className="signup-form" onSubmit={submit}><label>Email Address<input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email" /></label>{error && <div className="form-error">{error}</div>}<button className="primary-btn">Send Reset Link</button></form>}<Link to="/login" className="back-link">Back to Sign In</Link></section></main>;
}

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const nav = useNavigate();

  const token = params.get("token") || "";

  const [resetInfo, setResetInfo] = useState<{
    email: string;
    fullName: string;
    expiresAt: string;
  } | null>(null);

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);

  const passwordRules = {
    length: password.length >= 8,
    uppercase: /[A-Z]/.test(password),
    lowercase: /[a-z]/.test(password),
    number: /\d/.test(password),
  };

  const passwordValid =
    passwordRules.length &&
    passwordRules.uppercase &&
    passwordRules.lowercase &&
    passwordRules.number;

  const passwordsMatch =
    password.length > 0 &&
    password === confirm;

  useEffect(() => {
    async function loadResetInfo() {
      if (!token) {
        setError("Password reset link is invalid.");
        setLoading(false);
        return;
      }

      try {
        const response = await api.get<{
          email: string;
          fullName: string;
          expiresAt: string;
        }>("/auth/password-reset-info", {
          params: { token },
        });

        setResetInfo(response.data);
      } catch (requestError) {
        setError(getApiErrorMessage(requestError));
      } finally {
        setLoading(false);
      }
    }

    loadResetInfo();
  }, [token]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");

    if (!passwordValid) {
      setError("Please meet all password requirements.");
      return;
    }

    if (!passwordsMatch) {
      setError("Passwords do not match.");
      return;
    }

    setSubmitting(true);

    try {
      await resetPassword(token, password, confirm);
      setDone(true);
      setPassword("");
      setConfirm("");
    } catch (requestError) {
      setError(getApiErrorMessage(requestError));
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <main className="set-password-page">
        <section className="set-password-card">
          <div className="set-password-loading">
            Validating password reset link...
          </div>
        </section>
      </main>
    );
  }

  if (error && !resetInfo) {
    return (
      <main className="set-password-page">
        <section className="set-password-card set-password-invalid">
          <div className="set-password-invalid-icon">!</div>

          <h1>Invalid Password Reset Link</h1>

          <p>{error}</p>

          <button
            type="button"
            className="set-password-primary-btn"
            onClick={() =>
              nav("/forgot-password", {
                replace: true,
              })
            }
          >
            Request New Reset Link
          </button>
        </section>
      </main>
    );
  }

  if (done) {
    return (
      <main className="set-password-page">
        <section className="set-password-card set-password-success">
          <div className="set-password-success-icon">✓</div>

          <h1>Password Reset</h1>

          <p>
            Your password has been reset successfully. You can now sign in to
            the Business Partner&apos;s Portal using your registered email
            address.
          </p>

          <button
            type="button"
            className="set-password-primary-btn"
            onClick={() =>
              nav("/login", {
                replace: true,
              })
            }
          >
            Continue to Login
          </button>
        </section>
      </main>
    );
  }

  return (
    <main className="set-password-page">
      <section className="set-password-card">
        <div className="set-password-heading">
          <h1>Reset Your Password</h1>

          <p>
            Create a new secure password for your Business Partner&apos;s Portal
            account.
          </p>
        </div>

        <form className="set-password-form" onSubmit={submit}>
          <label className="set-password-field">
            <span>Email Address</span>

            <div className="set-password-input readonly">
              <span className="set-password-input-icon">✉</span>

              <input
                type="email"
                value={resetInfo?.email ?? ""}
                readOnly
                autoComplete="email"
              />
            </div>
          </label>

          <label className="set-password-field">
            <span>New Password</span>

            <div className="set-password-input">
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="new-password"
                placeholder="Enter new password"
                required
              />

              <button
                type="button"
                className="set-password-eye-btn"
                aria-label={showPassword ? "Hide password" : "Show password"}
                title={showPassword ? "Hide password" : "Show password"}
                onClick={() => setShowPassword((current) => !current)}
              >
                <svg
                  viewBox="0 0 24 24"
                  width="20"
                  height="20"
                  aria-hidden="true"
                  focusable="false"
                >
                  <path
                    d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.7"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                  <circle
                    cx="12"
                    cy="12"
                    r="2.75"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.7"
                  />
                  {!showPassword && (
                    <path
                      d="M4 4 20 20"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.7"
                      strokeLinecap="round"
                    />
                  )}
                </svg>
              </button>
            </div>
          </label>

          <div className="set-password-rules">
            <div className={passwordRules.length ? "valid" : ""}>
              <span>{passwordRules.length ? "✓" : "○"}</span>
              Minimum 8 characters
            </div>

            <div className={passwordRules.uppercase ? "valid" : ""}>
              <span>{passwordRules.uppercase ? "✓" : "○"}</span>
              At least one uppercase letter
            </div>

            <div className={passwordRules.lowercase ? "valid" : ""}>
              <span>{passwordRules.lowercase ? "✓" : "○"}</span>
              At least one lowercase letter
            </div>

            <div className={passwordRules.number ? "valid" : ""}>
              <span>{passwordRules.number ? "✓" : "○"}</span>
              At least one number
            </div>
          </div>

          <label className="set-password-field">
            <span>Confirm Password</span>

            <div className="set-password-input">
              <input
                type={showConfirmPassword ? "text" : "password"}
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
                autoComplete="new-password"
                placeholder="Confirm new password"
                required
              />

              <button
                type="button"
                className="set-password-eye-btn"
                aria-label={
                  showConfirmPassword ? "Hide password" : "Show password"
                }
                title={
                  showConfirmPassword ? "Hide password" : "Show password"
                }
                onClick={() =>
                  setShowConfirmPassword((current) => !current)
                }
              >
                <svg
                  viewBox="0 0 24 24"
                  width="20"
                  height="20"
                  aria-hidden="true"
                  focusable="false"
                >
                  <path
                    d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.7"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                  <circle
                    cx="12"
                    cy="12"
                    r="2.75"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.7"
                  />
                  {!showConfirmPassword && (
                    <path
                      d="M4 4 20 20"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.7"
                      strokeLinecap="round"
                    />
                  )}
                </svg>
              </button>
            </div>
          </label>

          {confirm.length > 0 && (
            <div
              className={`set-password-match ${
                passwordsMatch ? "valid" : "invalid"
              }`}
            >
              {passwordsMatch ? "✓ Passwords match" : "Passwords do not match"}
            </div>
          )}

          {error && <div className="form-error">{error}</div>}

          <button
            type="submit"
            className="set-password-primary-btn"
            disabled={
              submitting ||
              !passwordValid ||
              !passwordsMatch
            }
          >
            {submitting ? "Resetting..." : "Reset Password"}
          </button>
        </form>
      </section>
    </main>
  );
}

export function VerifyDevicePage() {
  const [params] = useSearchParams();
  const { setSession } = useAuth();
  const nav = useNavigate();
  const [digits, setDigits] = useState(["", "", "", "", "", ""]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [resending, setResending] = useState(false);
  const [challengeId, setChallengeId] = useState(params.get("challengeId") || "");
  const [seconds, setSeconds] = useState(60);
  const inputs = useRef<Array<HTMLInputElement | null>>([]);
  const maskedEmail = params.get("email") || "***";
  const otp = digits.join("");

  useEffect(() => {
    if (seconds <= 0) return;
    const timer = window.setInterval(() => setSeconds(value => value - 1), 1000);
    return () => window.clearInterval(timer);
  }, [seconds]);

  function changeDigit(index: number, value: string) {
    const clean = value.replace(/\D/g, "").slice(-1);
    setDigits(current => current.map((digit, i) => i === index ? clean : digit));
    setError("");
    if (clean && index < 5) inputs.current[index + 1]?.focus();
  }

  function keyDown(index: number, e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Backspace" && !digits[index] && index > 0) inputs.current[index - 1]?.focus();
  }

  function pasteOtp(e: React.ClipboardEvent<HTMLInputElement>) {
    const pasted = e.clipboardData.getData("text").replace(/\D/g, "").slice(0, 6);
    if (!pasted) return;
    e.preventDefault();
    const next = ["", "", "", "", "", ""];
    pasted.split("").forEach((digit, index) => { next[index] = digit; });
    setDigits(next);
    inputs.current[Math.min(pasted.length, 6) - 1]?.focus();
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (otp.length !== 6) { setError("Please enter the complete 6 digit registration code."); return; }
    setBusy(true); setError("");
    try {
      const session = await verifyLoginOtp(challengeId, otp);
      setSession(session);
      nav(session.user.userType === "VENDOR" ? "/vendor" : session.user.userType === "ADMIN" ? "/admin" : "/internal", { replace: true });
    } catch (e) { setError(getApiErrorMessage(e)); }
    finally { setBusy(false); }
  }

  async function resend() {
    if (seconds > 0 || resending) return;
    setError(""); setResending(true);
    try {
      const next = await resendLoginOtp(challengeId);
      setChallengeId(next.challengeId);
      setDigits(["", "", "", "", "", ""]);
      setSeconds(60);
      inputs.current[0]?.focus();
    } catch (e) { setError(getApiErrorMessage(e)); }
    finally { setResending(false); }
  }

  return (
    <main className="auth-page otp-page">
      <section className="otp-card">
        <div className="auth-brand-logo otp-brand-logo">
          <img src="/pakistan-cables-logo.png" alt="Pakistan Cables" />
        </div>
        <h1>Device Registration</h1>
        <p>Enter the one-time registration code sent to <strong>{maskedEmail}</strong></p>
        <form onSubmit={submit}>
          <div className="otp-boxes" onPaste={pasteOtp}>
            {digits.map((digit, index) => (
              <input key={index} ref={el => { inputs.current[index] = el; }} value={digit}
                onChange={e => changeDigit(index, e.target.value)} onKeyDown={e => keyDown(index, e)}
                inputMode="numeric" maxLength={1} autoComplete={index === 0 ? "one-time-code" : "off"}
                aria-label={`OTP digit ${index + 1}`} autoFocus={index === 0} />
            ))}
          </div>
          {error && <div className="form-error otp-error">{error}</div>}
          <div className="otp-resend">
            {seconds > 0 ? <>Resend code in <strong>00:{String(seconds).padStart(2, "0")}</strong></> : <button type="button" onClick={resend} disabled={resending}>{resending ? "Sending..." : "Resend Code"}</button>}
          </div>
          <button className="primary-btn otp-verify-btn" disabled={busy || otp.length !== 6}>{busy ? "Verifying..." : "Verify"}</button>
        </form>
        <Link className="back-link" to="/login">Back to Sign In</Link>
      </section>
    </main>
  );
}
