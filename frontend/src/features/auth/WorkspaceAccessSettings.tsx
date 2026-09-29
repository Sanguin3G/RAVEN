import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ApiError } from "../../api/client";
import { createWorkspaceUser, listWorkspaceUsers, setWorkspaceUserEnabled, updateWorkspaceUserRole, type WorkspaceUser } from "./authApi";
import styles from "./auth.module.css";

export function WorkspaceAccessSettings() {
  const [users, setUsers] = useState<WorkspaceUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyUserId, setBusyUserId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [temporaryPassword, setTemporaryPassword] = useState("");
  const [role, setRole] = useState<WorkspaceUser["role"]>("Researcher");
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setUsers(await listWorkspaceUsers());
      setError(null);
    } catch {
      setError("Could not load workspace users.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void reload(); }, [reload]);

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCreating(true);
    setError(null);
    setMessage(null);
    try {
      const created = await createWorkspaceUser({ displayName, email, temporaryPassword, role });
      setUsers((current) => [...current, created].sort((left, right) => left.email.localeCompare(right.email)));
      setDisplayName("");
      setEmail("");
      setTemporaryPassword("");
      setRole("Researcher");
      setMessage(`Created ${created.role} account for ${created.email}.`);
    } catch (reason) {
      setError(errorMessage(reason, "Could not create this workspace user."));
    } finally {
      setCreating(false);
    }
  }

  async function changeRole(user: WorkspaceUser, nextRole: WorkspaceUser["role"]) {
    setBusyUserId(user.id);
    setError(null);
    setMessage(null);
    try {
      const updated = await updateWorkspaceUserRole(user.id, nextRole);
      setUsers((current) => current.map((item) => item.id === updated.id ? updated : item));
    } catch (reason) {
      setError(errorMessage(reason, "Could not update this user's role."));
    } finally {
      setBusyUserId(null);
    }
  }

  async function toggleEnabled(user: WorkspaceUser) {
    setBusyUserId(user.id);
    setError(null);
    setMessage(null);
    try {
      const updated = await setWorkspaceUserEnabled(user.id, !user.isEnabled);
      setUsers((current) => current.map((item) => item.id === updated.id ? updated : item));
    } catch (reason) {
      setError(errorMessage(reason, "Could not update this user's status."));
    } finally {
      setBusyUserId(null);
    }
  }

  return <section className={`panel ${styles.workspaceAccessCard}`} aria-labelledby="workspace-access-heading">
    <p className="eyebrow">ADMINISTRATION</p>
    <h2 id="workspace-access-heading">Workspace access</h2>
    <p>Create and manage the private workspace accounts. New members are Researchers by default.</p>
    {error ? <p className="form-error" role="alert">{error}</p> : null}
    {message ? <p role="status">{message}</p> : null}

    <form className={styles.workspaceCreateForm} onSubmit={create}>
      <h3>Create member</h3>
      <label htmlFor="member-name">Name</label>
      <input id="member-name" className="text-input" required maxLength={160} value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
      <label htmlFor="member-email">Email</label>
      <input id="member-email" className="text-input" type="email" autoComplete="email" required maxLength={256} value={email} onChange={(event) => setEmail(event.target.value)} />
      <label htmlFor="member-password">Temporary password</label>
      <input id="member-password" className="text-input" type="password" autoComplete="new-password" required minLength={12} maxLength={128} value={temporaryPassword} onChange={(event) => setTemporaryPassword(event.target.value)} />
      <label htmlFor="member-role">Role</label>
      <select id="member-role" className="text-input" value={role} onChange={(event) => setRole(event.target.value as WorkspaceUser["role"])}><option value="Researcher">Researcher</option><option value="Admin">Admin</option></select>
      <button className="button" type="submit" disabled={creating}>{creating ? "Creating…" : "Create member"}</button>
    </form>

    <div className={styles.workspaceMembers}>
      <h3>Workspace members</h3>
      {loading ? <p role="status">Loading members…</p> : users.length === 0 ? <p>No workspace users were found.</p> : <div className="table-wrap"><table>
        <thead><tr><th scope="col">Member</th><th scope="col">Role</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
        <tbody>{users.map((user) => <tr key={user.id}>
          <td><strong>{user.displayName}</strong><small className="table-secondary">{user.email}</small></td>
          <td><select className="text-input" aria-label={`Role for ${user.email}`} value={user.role} disabled={busyUserId === user.id} onChange={(event) => void changeRole(user, event.target.value as WorkspaceUser["role"])}><option value="Researcher">Researcher</option><option value="Admin">Admin</option></select></td>
          <td>{user.isEnabled ? "Enabled" : "Disabled"}</td>
          <td><button className="button button--quiet" type="button" disabled={busyUserId === user.id} onClick={() => void toggleEnabled(user)}>{user.isEnabled ? "Disable" : "Enable"}</button></td>
        </tr>)}</tbody>
      </table></div>}
      <button className="button button--quiet" type="button" onClick={() => void reload()} disabled={loading}>Refresh members</button>
    </div>
  </section>;
}

function errorMessage(reason: unknown, fallback: string) {
  if (!(reason instanceof ApiError)) return fallback;
  if (reason.problem?.errors) return Object.values(reason.problem.errors).flat().join(" ") || fallback;
  return reason.problem?.detail ?? fallback;
}
