import {
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  useNavigate,
  useSearchParams,
} from "react-router-dom";

import {
  api,
  getApiErrorMessage,
} from "../api/client";

type SetupInfo = {
  email: string;
  fullName: string;
  expiresAt: string;
};

export function SetPasswordPage() {
  const navigate =
    useNavigate();

  const [
    searchParams,
  ] =
    useSearchParams();

  const token =
    useMemo(
      () =>
        searchParams.get(
          "token"
        ) ?? "",
      [searchParams]
    );

  const [
    setupInfo,
    setSetupInfo,
  ] =
    useState<
      SetupInfo | null
    >(null);

  const [
    password,
    setPassword,
  ] =
    useState("");

  const [
    confirmPassword,
    setConfirmPassword,
  ] =
    useState("");

  const [
    showPassword,
    setShowPassword,
  ] =
    useState(false);

  const [
    showConfirmPassword,
    setShowConfirmPassword,
  ] =
    useState(false);

  const [
    loading,
    setLoading,
  ] =
    useState(true);

  const [
    submitting,
    setSubmitting,
  ] =
    useState(false);

  const [
    error,
    setError,
  ] =
    useState("");

  const [
    success,
    setSuccess,
  ] =
    useState(false);

  const passwordRules =
    useMemo(
      () => ({
        length:
          password.length >= 8,

        uppercase:
          /[A-Z]/.test(
            password
          ),

        lowercase:
          /[a-z]/.test(
            password
          ),

        number:
          /\d/.test(
            password
          ),
      }),
      [password]
    );

  const passwordValid =
    passwordRules.length
    &&
    passwordRules.uppercase
    &&
    passwordRules.lowercase
    &&
    passwordRules.number;

  const passwordsMatch =
    password.length > 0
    &&
    password ===
      confirmPassword;

  // ============================================================
  // VALIDATE SETUP TOKEN + LOAD EMAIL
  // ============================================================

  useEffect(() => {
    async function load() {
      if (!token) {
        setError(
          "Password setup link is invalid."
        );

        setLoading(false);

        return;
      }

      try {
        const response =
          await api.get<SetupInfo>(
            "/auth/password-setup-info",
            {
              params: {
                token,
              },
            }
          );

        setSetupInfo(
          response.data
        );
      } catch (requestError) {
        setError(
          getApiErrorMessage(
            requestError
          )
        );
      } finally {
        setLoading(false);
      }
    }

    load();
  }, [token]);

  // ============================================================
  // SET PASSWORD
  // ============================================================

  async function handleSubmit(
    event: React.FormEvent
  ) {
    event.preventDefault();

    setError("");

    if (!passwordValid) {
      setError(
        "Please meet all password requirements."
      );

      return;
    }

    if (!passwordsMatch) {
      setError(
        "Passwords do not match."
      );

      return;
    }

    setSubmitting(true);

    try {
      await api.post(
        "/auth/set-password",
        {
          token,
          password,
          confirmPassword,
        }
      );

      setSuccess(true);

      setPassword("");

      setConfirmPassword("");
    } catch (requestError) {
      setError(
        getApiErrorMessage(
          requestError
        )
      );
    } finally {
      setSubmitting(false);
    }
  }

  // ============================================================
  // LOADING
  // ============================================================

  if (loading) {
    return (
      <main className="set-password-page">
        <section className="set-password-card">
          <div className="set-password-loading">
            Validating password setup link...
          </div>
        </section>
      </main>
    );
  }

  // ============================================================
  // INVALID LINK
  // ============================================================

  if (
    error
    &&
    !setupInfo
  ) {
    return (
      <main className="set-password-page">
        <section className="set-password-card set-password-invalid">
          <div className="set-password-invalid-icon">
            !
          </div>

          <h1>
            Invalid Password Setup Link
          </h1>

          <p>
            {error}
          </p>

          <button
            type="button"
            className="set-password-primary-btn"
            onClick={() =>
              navigate(
                "/login",
                {
                  replace:
                    true,
                }
              )
            }
          >
            Go to Login
          </button>
        </section>
      </main>
    );
  }

  // ============================================================
  // SUCCESS
  // ============================================================

  if (success) {
    return (
      <main className="set-password-page">
        <section className="set-password-card set-password-success">
          <div className="set-password-success-icon">
            ✓
          </div>

          <h1>
            Password Created
          </h1>

          <p>
            Your password has been created
            successfully. You can now sign
            in to the Business Partner&apos;s
            Portal using your registered
            email address.
          </p>

          <button
            type="button"
            className="set-password-primary-btn"
            onClick={() =>
              navigate(
                "/login",
                {
                  replace:
                    true,
                }
              )
            }
          >
            Continue to Login
          </button>
        </section>
      </main>
    );
  }

  // ============================================================
  // FORM
  // ============================================================

  return (
    <main className="set-password-page">
      <section className="set-password-card">
        <div className="set-password-heading">
          <h1>
            Set Your Password
          </h1>

          <p>
            Create a secure password to
            activate your Business
            Partner&apos;s Portal account.
          </p>
        </div>

        <form
          className="set-password-form"
          onSubmit={
            handleSubmit
          }
        >
          <label className="set-password-field">
            <span>
              Email Address
            </span>

            <div className="set-password-input readonly">
              <span className="set-password-input-icon">
                ✉
              </span>

              <input
                type="email"
                value={
                  setupInfo
                    ?.email
                  ?? ""
                }
                readOnly
              />
            </div>
          </label>

          <label className="set-password-field">
            <span>
              New Password
            </span>

            <div className="set-password-input">
              <input
                type={
                  showPassword
                    ? "text"
                    : "password"
                }
                value={
                  password
                }
                onChange={(
                  event
                ) =>
                  setPassword(
                    event.target
                      .value
                  )
                }
                autoComplete="new-password"
                placeholder="Enter new password"
                required
              />

              <button
                type="button"
                className="set-password-eye-btn"
                onClick={() =>
                  setShowPassword(
                    (
                      current
                    ) =>
                      !current
                  )
                }
              >
                {showPassword
                  ? "◉"
                  : "◎"}
              </button>
            </div>
          </label>

          <div className="set-password-rules">
            <div
              className={
                passwordRules.length
                  ? "valid"
                  : ""
              }
            >
              <span>
                {passwordRules.length
                  ? "✓"
                  : "○"}
              </span>

              Minimum 8 characters
            </div>

            <div
              className={
                passwordRules.uppercase
                  ? "valid"
                  : ""
              }
            >
              <span>
                {passwordRules.uppercase
                  ? "✓"
                  : "○"}
              </span>

              At least one uppercase letter
            </div>

            <div
              className={
                passwordRules.lowercase
                  ? "valid"
                  : ""
              }
            >
              <span>
                {passwordRules.lowercase
                  ? "✓"
                  : "○"}
              </span>

              At least one lowercase letter
            </div>

            <div
              className={
                passwordRules.number
                  ? "valid"
                  : ""
              }
            >
              <span>
                {passwordRules.number
                  ? "✓"
                  : "○"}
              </span>

              At least one number
            </div>
          </div>

          <label className="set-password-field">
            <span>
              Confirm Password
            </span>

            <div className="set-password-input">
              <input
                type={
                  showConfirmPassword
                    ? "text"
                    : "password"
                }
                value={
                  confirmPassword
                }
                onChange={(
                  event
                ) =>
                  setConfirmPassword(
                    event.target
                      .value
                  )
                }
                autoComplete="new-password"
                placeholder="Confirm new password"
                required
              />

              <button
                type="button"
                className="set-password-eye-btn"
                onClick={() =>
                  setShowConfirmPassword(
                    (
                      current
                    ) =>
                      !current
                  )
                }
              >
                {showConfirmPassword
                  ? "◉"
                  : "◎"}
              </button>
            </div>
          </label>

          {confirmPassword && (
            <div
              className={
                passwordsMatch
                  ? "set-password-match valid"
                  : "set-password-match invalid"
              }
            >
              {passwordsMatch
                ? "✓ Passwords match"
                : "Passwords do not match"}
            </div>
          )}

          {error && (
            <div className="form-error">
              {error}
            </div>
          )}

          <button
            type="submit"
            className="set-password-primary-btn"
            disabled={
              submitting
              ||
              !passwordValid
              ||
              !passwordsMatch
            }
          >
            {submitting
              ? "Setting Password..."
              : "Set Password"}
          </button>
        </form>
      </section>
    </main>
  );
}