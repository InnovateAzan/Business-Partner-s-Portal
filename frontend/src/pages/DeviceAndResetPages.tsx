import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { requestPasswordReset, resetPassword, resendLoginOtp, verifyLoginOtp } from "../api/auth";
import { getApiErrorMessage } from "../api/client";
import { useAuth } from "../auth/AuthContext";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState(""); const [sent, setSent] = useState(false); const [error, setError] = useState("");
  async function submit(e: React.FormEvent) { e.preventDefault(); setError(""); try { await requestPasswordReset(email); setSent(true); } catch (e) { setError(getApiErrorMessage(e)); } }
  return <main className="auth-page"><section className="signup-card"><h1>Forgot Password</h1>{sent ? <p className="success-note">If an account exists for that email address, a password reset link has been sent.</p> : <form className="signup-form" onSubmit={submit}><label>Email Address<input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email" /></label>{error && <div className="form-error">{error}</div>}<button className="primary-btn">Send Reset Link</button></form>}<Link to="/login" className="back-link">Back to Sign In</Link></section></main>;
}

export function ResetPasswordPage() {
  const [params] = useSearchParams(); const nav = useNavigate(); const [password, setPassword] = useState(""); const [confirm, setConfirm] = useState(""); const [error, setError] = useState(""); const [done, setDone] = useState(false);
  async function submit(e: React.FormEvent) { e.preventDefault(); setError(""); try { await resetPassword(params.get("token") || "", password, confirm); setDone(true); setTimeout(() => nav("/login"), 1200); } catch (e) { setError(getApiErrorMessage(e)); } }
  return <main className="auth-page"><section className="signup-card"><h1>Reset Password</h1>{done ? <p className="success-note">Password reset. Redirecting to sign in…</p> : <form className="signup-form" onSubmit={submit}><label>New Password<input type="password" value={password} onChange={e => setPassword(e.target.value)} required autoComplete="new-password" /></label><label>Confirm Password<input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} required autoComplete="new-password" /></label>{error && <div className="form-error">{error}</div>}<button className="primary-btn">Reset Password</button></form>}</section></main>;
}

export function VerifyDevicePage() {
  const [params] = useSearchParams(); const { setSession } = useAuth(); const nav = useNavigate(); const [otp, setOtp] = useState(""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [challengeId, setChallengeId] = useState(params.get("challengeId") || "");
  async function submit(e: React.FormEvent) { e.preventDefault(); setBusy(true); setError(""); try { const session = await verifyLoginOtp(challengeId, otp); setSession(session); nav(session.user.userType === "VENDOR" ? "/vendor" : session.user.userType === "ADMIN" ? "/admin" : "/internal", { replace: true }); } catch (e) { setError(getApiErrorMessage(e)); } finally { setBusy(false); } }
  async function resend() { setError(""); try { const next = await resendLoginOtp(challengeId); setChallengeId(next.challengeId); } catch (e) { setError(getApiErrorMessage(e)); } }
  return <main className="auth-page"><section className="signup-card"><h1>Verify Your Device</h1><p>We&apos;ve sent a verification code to your registered email ({params.get("email") || "***"}).</p><form className="signup-form" onSubmit={submit}><label>6 digit OTP<input value={otp} onChange={e => setOtp(e.target.value.replace(/\D/g, "").slice(0, 6))} inputMode="numeric" pattern="[0-9]{6}" required autoFocus /></label>{error && <div className="form-error">{error}</div>}<button className="primary-btn" disabled={busy}>{busy ? "Verifying..." : "Verify Device"}</button></form><button type="button" className="back-link" onClick={resend}>Resend Code</button><Link className="back-link" to="/login">Back to Sign In</Link></section></main>;
}
