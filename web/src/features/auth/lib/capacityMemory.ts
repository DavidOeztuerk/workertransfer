import type { Membership } from "../types/session";

/**
 * What this browser last acted as — so a sign-in lands where one left off.
 *
 * <strong>A convenience, never a claim.</strong> What is stored is a request
 * the switcher would have sent anyway; the server checks the membership on
 * every switch (ADR-0018). A stale entry costs one refused switch, after which
 * it is forgotten.
 *
 * Every access is wrapped: storage throws in a private window and comes back
 * empty after clearing site data, and the page must work without it.
 */
const KEY = (userId: string) => `wt.actingAs.${userId}`;
const SELF = "self";

export function rememberCapacity(userId: string, tenantId: string | null): void {
  restoredFor = userId;
  try {
    localStorage.setItem(KEY(userId), tenantId ?? SELF);
  } catch {
    // Not remembered; the next sign-in starts as the person.
  }
}

export function forgetCapacity(userId: string): void {
  try {
    localStorage.removeItem(KEY(userId));
  } catch {
    // Nothing stored that could be forgotten.
  }
}

function remembered(userId: string): string | null {
  try {
    return localStorage.getItem(KEY(userId));
  } catch {
    return null;
  }
}

// Once per sign-in, in memory as well: if storage refuses to keep "self", a
// founder would otherwise be switched back to the company at every render.
let restoredFor: string | null = null;

/** A fresh sign-in may be restored again. */
export function capacityRestoreReset(): void {
  restoredFor = null;
}

/**
 * The company a fresh person session should switch to, or `null`.
 *
 * The remembered choice wins. Without one, whoever has exactly one company and
 * is its `admin` — who registered it — starts acting for it: that account
 * exists for the company. Anyone else starts as themselves, the normal case on
 * a transfer market (ADR-0017).
 */
export function companyToRestore(userId: string, memberships: Membership[]): string | null {
  if (restoredFor === userId) return null;
  restoredFor = userId;

  const choice = remembered(userId);
  if (choice === SELF) return null;
  if (choice !== null) {
    if (memberships.some((company) => company.id === choice)) return choice;
    forgetCapacity(userId);
    return null;
  }

  const [only, ...more] = memberships;
  return only !== undefined && more.length === 0 && only.role === "admin" ? only.id : null;
}
