import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { ApiError } from "../../api/client";
import { useAuth } from "./AuthProvider";
import styles from "./auth.module.css";

function safeReturnTo(value: unknown) {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//")) return "/";
  return value;
}

export function LoginPage() {
  const { status, login } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const stateFrom = (location.state as { from?: { pathname?: unknown; search?: unknown; hash?: unknown } } | null)?.from;
  const queryReturn = new URLSearchParams(location.search).get("returnTo");
  const destination = safeReturnTo(stateFrom?.pathname)
    + (typeof stateFrom?.search === "string" ? stateFrom.search : "")
    + (typeof stateFrom?.hash === "string" ? stateFrom.hash : "");

  if (status === "loading") return <main className={styles.loginPage}><p role="status">Checking your workspace session…</p></main>;
  if (status === "authenticated") return <Navigate to={destination !== "/" ? destination : safeReturnTo(queryReturn)} replace />;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await login(email, password);
      navigate(destination !== "/" ? destination : safeReturnTo(queryReturn), { replace: true });
    } catch (reason) {
      setError(reason instanceof ApiError && reason.status === 401
        ? "Email or password is incorrect, or this account is disabled."
        : "RAVEN could not sign you in. Check the API connection and try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return <main className={styles.loginPage}>
    <section className={styles.loginCard} aria-labelledby="login-heading">
      <p className="eyebrow">PRIVATE WORKSPACE</p>
      <h1 id="login-heading">Sign in to RAVEN</h1>
      <p className={styles.loginIntro}>Use the account provided by your workspace administrator.</p>
      <form className={styles.loginForm} onSubmit={submit}>
        <label htmlFor="login-email">Email</label>
        <input id="login-email" className="text-input" type="email" autoComplete="username" required maxLength={256} value={email} onChange={(event) => setEmail(event.target.value)} />
        <label htmlFor="login-password">Password</label>
        <input id="login-password" className="text-input" type="password" autoComplete="current-password" required maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} />
        {error ? <p className="form-error" role="alert">{error}</p> : null}
        <button className="button" type="submit" disabled={submitting}>{submitting ? "Signing in…" : "Sign in"}</button>
      </form>
    </section>
  </main>;
}
