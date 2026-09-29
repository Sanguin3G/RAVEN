import { useState, type FormEvent } from "react";
import { ApiError } from "../../api/client";
import { changePassword } from "./authApi";
import { useAuth } from "./AuthProvider";
import styles from "./auth.module.css";

export function AccountSettings() {
  const { user } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setMessage(null);
    if (newPassword !== confirmation) {
      setError("The new passwords do not match.");
      return;
    }
    setBusy(true);
    try {
      await changePassword(currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      setConfirmation("");
      setMessage("Password changed.");
    } catch (reason) {
      const details = reason instanceof ApiError ? reason.problem?.errors : undefined;
      const validation = details ? Object.values(details).flat().join(" ") : "";
      setError(validation || (reason instanceof ApiError && reason.status === 401
        ? "The current password is incorrect."
        : "RAVEN could not change the password. Try again."));
    } finally {
      setBusy(false);
    }
  }

  return <section className={`panel ${styles.accountCard}`} aria-labelledby="account-settings-heading">
    <p className="eyebrow">ACCOUNT</p>
    <h2 id="account-settings-heading">Account</h2>
    <p>Signed in as <strong>{user?.displayName}</strong> · {user?.email}</p>
    <p>Role: {user?.roles.join(", ") || "Workspace member"}</p>
    <form className={styles.accountForm} onSubmit={submit}>
      <h3>Change password</h3>
      <label htmlFor="current-password">Current password</label>
      <input id="current-password" className="text-input" type="password" autoComplete="current-password" required maxLength={128} value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} />
      <label htmlFor="new-password">New password</label>
      <input id="new-password" className="text-input" type="password" autoComplete="new-password" required minLength={12} maxLength={128} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
      <label htmlFor="confirm-password">Confirm new password</label>
      <input id="confirm-password" className="text-input" type="password" autoComplete="new-password" required minLength={12} maxLength={128} value={confirmation} onChange={(event) => setConfirmation(event.target.value)} />
      {error ? <p className="form-error" role="alert">{error}</p> : null}
      {message ? <p role="status">{message}</p> : null}
      <button className="button" type="submit" disabled={busy}>{busy ? "Changing…" : "Change password"}</button>
    </form>
  </section>;
}
