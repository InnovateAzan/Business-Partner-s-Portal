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
    <main className="login-page login-reference-page">
      <div className="login-card login-reference-card">
        <section className="login-reference-left">
          <div className="login-reference-dots" aria-hidden="true" />
          <div className="login-reference-brand"><div className="login-reference-logo"><img src="/pakistan-cables-logo.png" alt="Pakistan Cables" /></div><div className="login-reference-brand-name">PAKISTAN CABLES</div></div>
          <div className="login-reference-copy">
            <p className="login-reference-welcome">Welcome to</p>
            <h1>Business Partner&apos;s<br />Portal</h1>
            <p className="login-reference-description">Access purchase orders, GRNs, invoices and payment status securely.</p>
          </div>
          <div className="login-reference-hero">
            <img className="login-reference-hero-glow" src="/Factory.png" alt="" aria-hidden="true" />
            <img className="login-reference-hero-image" src="/Factory.png" alt="Pakistan Cables factory" />
          </div>
          <div className="login-reference-bottom-bar" />
        </section>
        <section className="login-reference-right">
          <div className="login-reference-secure">
            <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="2.3" aria-hidden="true"><path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="m9 12 2 2 4-4"/></svg>
            Secure Vendor Portal
          </div>
          <h2>Sign In</h2>
          <p className="login-reference-lead">Continue to Business Partner&apos;s Portal</p>
          <form className="login-reference-form" onSubmit={handleSubmit}>
            <label htmlFor="login-email">Email Address</label>
            <div className="login-reference-input">
              <span className="login-reference-icon"><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg></span>
              <input id="login-email" type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder="you@company.com" autoComplete="username" required />
            </div>
            <label htmlFor="login-password">Password</label>
            <div className="login-reference-input">
              <span className="login-reference-icon"><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg></span>
              <input id="login-password" type={showPassword ? "text" : "password"} value={password} onChange={e => setPassword(e.target.value)} placeholder="Enter your password" autoComplete="current-password" required />
              <button className="login-reference-eye" type="button" onClick={() => setShowPassword(v => !v)} aria-label={showPassword ? "Hide password" : "Show password"}><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="currentColor" strokeWidth="2"><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>{showPassword && <path d="M3 3 21 21"/>}</svg></button>
            </div>
            <button className="login-reference-forgot" type="button" onClick={handleForgotPassword}>Forgot Password?</button>
            {error && <p className="login-reference-error" role="alert">{error}</p>}
            <button className="login-reference-submit" type="submit" disabled={loading}>
              {loading ? "Signing in..." : <><svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" strokeWidth="2"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg>Sign In</>}
            </button>
          </form>
          <div className="login-reference-note"><svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><path d="M12 11v6M12 7v1"/></svg><span>Vendor must already exist in Oracle EBS before registration.</span></div>
        </section>
      </div>
    </main>
  );
}
