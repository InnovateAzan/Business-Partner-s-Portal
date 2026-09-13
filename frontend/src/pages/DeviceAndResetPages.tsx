import { useEffect, useRef, useState } from "react";
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
    if (otp.length !== 6) { setError("Please enter the complete 6 digit OTP."); return; }
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
        <div className="otp-illustration" aria-hidden="true">
          <span className="otp-phone">▯</span><span className="otp-check">✓</span>
        </div>
        <h1>OTP Verification</h1>
        <p>Enter the OTP sent to <strong>{maskedEmail}</strong></p>
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
            {seconds > 0 ? <>Resend code in <strong>00:{String(seconds).padStart(2, "0")}</strong></> : <button type="button" onClick={resend} disabled={resending}>{resending ? "Sending..." : "Resend OTP"}</button>}
          </div>
          <button className="primary-btn otp-verify-btn" disabled={busy || otp.length !== 6}>{busy ? "Verifying..." : "Verify"}</button>
        </form>
        <Link className="back-link" to="/login">Back to Sign In</Link>
      </section>
    </main>
  );
}
