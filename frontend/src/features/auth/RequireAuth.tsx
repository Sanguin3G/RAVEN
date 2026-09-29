import type { ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useAuth } from "./AuthProvider";
import styles from "./auth.module.css";

export function RequireAuth({ children }: { children?: ReactNode }) {
  const { status, error, refresh } = useAuth();
  const location = useLocation();

  if (status === "loading") return <main className={styles.authState}><p role="status">Loading your workspace…</p></main>;
  if (status === "unavailable") return <main className={styles.authState}><p className="form-error" role="alert">{error}</p><button className="button" type="button" onClick={() => void refresh()}>Retry</button></main>;
  if (status === "anonymous") return <Navigate to="/login" state={{ from: location }} replace />;
  return children ?? <Outlet />;
}
