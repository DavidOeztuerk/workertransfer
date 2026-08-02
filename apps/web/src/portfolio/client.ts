// Client für portfolio-service.
//
// Wie beim Profil: eine Freigabe für alle Unternehmen. Die Capability ist
// trotzdem eine eigene — sonst wäre „ich bin ansprechbar" stillschweigend auch
// „schaut euch meine Arbeiten an".

import {
  type ConsentResult,
  PORTFOLIO_VISIBILITY,
  isGranted,
  setGranted,
} from "../consent/client";
import { PORTFOLIO_BASE_URL } from "../env";

export interface PortfolioItem {
  title: string;
  summary: string;
  /** `null` heißt „kein Link", nicht „leerer Link". */
  url: string | null;
  role: string;
  year: number | null;
}

export interface Portfolio {
  subject_id: string;
  items: PortfolioItem[];
  updated_at: string;
}

export type SaveResult =
  | { ok: true; portfolio: Portfolio }
  | { ok: false; reason: "unauthenticated" | "invalid" | "offline"; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      if (Array.isArray(detail)) {
        const first: unknown = detail[0];
        if (typeof first === "object" && first !== null) {
          const msg = (first as { msg?: unknown }).msg;
          if (typeof msg === "string") return msg;
        }
      }
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

export async function getMyPortfolio(): Promise<Portfolio | null> {
  try {
    const res = await fetch(`${PORTFOLIO_BASE_URL}/portfolios/me`, { credentials: "include" });
    if (!res.ok) return null;
    const body: unknown = await res.json();
    return body === null ? null : (body as Portfolio);
  } catch {
    return null;
  }
}

export async function saveMyPortfolio(items: PortfolioItem[]): Promise<SaveResult> {
  let res: Response;
  try {
    res = await fetch(`${PORTFOLIO_BASE_URL}/portfolios/me`, {
      method: "PUT",
      credentials: "include",
      headers: { "content-type": "application/json" },
      // Genau das eine Feld des Vertrags. Kein Sichtbarkeitsfeld: das wäre eine
      // zweite Wahrheit neben dem Ledger.
      body: JSON.stringify({ items }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Das Portfolio konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, portfolio: (await res.json()) as Portfolio };
}

export function getPortfolioVisibility(subjectId: string): Promise<boolean> {
  return isGranted(subjectId, PORTFOLIO_VISIBILITY);
}

export function setPortfolioVisibility(
  subjectId: string,
  granted: boolean
): Promise<ConsentResult> {
  return setGranted(
    subjectId,
    PORTFOLIO_VISIBILITY,
    granted,
    "Über die Portfolio-Einstellungen zurückgezogen"
  );
}

export type PortfolioResult =
  | { ok: true; portfolio: Portfolio | null }
  | { ok: false; message: string };

/**
 * Das Portfolio einer anderen Person.
 *
 * `404` wird zu `portfolio: null`, weil „gibt es nicht" und „nicht freigegeben"
 * für die Oberfläche derselbe Fall sind — genau so, wie der Server sie
 * ununterscheidbar hält. `503` bleibt ein eigener Zustand.
 */
export async function getPortfolio(subjectId: string): Promise<PortfolioResult> {
  try {
    const res = await fetch(`${PORTFOLIO_BASE_URL}/portfolios/${subjectId}`, {
      credentials: "include",
    });
    if (res.status === 404) return { ok: true, portfolio: null };
    if (res.status === 503) {
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) {
      return { ok: false, message: "Das Portfolio ließ sich nicht laden." };
    }
    return { ok: true, portfolio: (await res.json()) as Portfolio };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
