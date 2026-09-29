import { clearAntiforgeryToken, request } from "../../api/client";

export interface AuthUser {
  id: string;
  displayName: string;
  email: string;
  roles: string[];
}

export interface WorkspaceUser {
  id: string;
  displayName: string;
  email: string;
  role: "Admin" | "Researcher";
  isEnabled: boolean;
  createdAt: string;
}

export interface CreateWorkspaceUser {
  displayName: string;
  email: string;
  temporaryPassword: string;
  role: "Admin" | "Researcher";
}

export function getCurrentUser() {
  return request<AuthUser>("/api/auth/me");
}

export function login(email: string, password: string) {
  return request<AuthUser>("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });
}

export async function logout() {
  try {
    await request<void>("/api/auth/logout", { method: "POST" });
  } finally {
    clearAntiforgeryToken();
  }
}

export function changePassword(currentPassword: string, newPassword: string) {
  return request<void>("/api/auth/change-password", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ currentPassword, newPassword }),
  });
}

export function listWorkspaceUsers() {
  return request<WorkspaceUser[]>("/api/admin/users");
}

export function createWorkspaceUser(user: CreateWorkspaceUser) {
  return request<WorkspaceUser>("/api/admin/users", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(user),
  });
}

export function updateWorkspaceUserRole(id: string, role: WorkspaceUser["role"]) {
  return request<WorkspaceUser>(`/api/admin/users/${encodeURIComponent(id)}/role`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ role }),
  });
}

export function setWorkspaceUserEnabled(id: string, isEnabled: boolean) {
  return request<WorkspaceUser>(`/api/admin/users/${encodeURIComponent(id)}/enabled`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ isEnabled }),
  });
}
