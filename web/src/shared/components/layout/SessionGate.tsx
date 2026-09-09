import { useEffect, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { onSessionExpired } from "../../../core/api/client";
import { sitzungBeendet } from "../../../features/auth/store/authSlice";
import { loadSession } from "../../../features/auth/store/authThunks";

/**
 * Access-JWT lebt fünfzehn Minuten (Girder `ExpireMinutes`). Zwei Minuten
 * Puffer, weil der Cookie httpOnly ist und die Oberfläche `exp` nicht lesen
 * kann. Visibility/Focus fängt den Deckel des Laptops.
 */
const ERNEUERUNG_NACH_MS = 13 * 60 * 1000;

const OEFFENTLICHE_PFADE = [
  "/",
  "/login",
  "/register",
  "/verify",
  "/invitation",
  "/jobs",
  "/imprint",
  "/privacy",
  "/terms",
  "/accessibility",
];

function istGeschuetzterPfad(pathname: string): boolean {
  if (pathname.startsWith("/careers/")) return false;
  return !OEFFENTLICHE_PFADE.some((offen) => pathname === offen);
}

/**
 * Fragt, wer gerade handelt — und hält die Sitzung am Leben.
 *
 * Wenn das Token abgelaufen ist (z. B. nach Docker-Neubau oder Ablauf ohne
 * Erneuerung), fällt die Sitzung sofort und der Benutzer wird sauber auf die
 * Startseite navigiert, statt auf einer geschützten Seite mit Fehlermeldungen
 * zu verharren.
 *
 * <strong>Es blockiert die Seite nicht.</strong> Ein Ladebildschirm über der
 * ganzen Anwendung, nur um zu erfahren, ob jemand angemeldet ist, macht jeden
 * Kaltstart langsam — auch für die Marketingseite, die niemanden kennen muss.
 *
 * Tokens bleiben im httpOnly-Cookie. Hier wird kein JWT dekodiert und nichts
 * in `localStorage` gelegt.
 */
export function SessionGate({ children }: { children: React.ReactNode }) {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const location = useLocation();
  const status = useAppSelector((state) => state.auth.status);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const vorigerStatus = useRef(status);

  // 1. Wenn ein Aufruf feststellt, dass Token/Refresh tot sind:
  // Sitzung sofort beenden und auf Startseite navigieren.
  useEffect(() => {
    return onSessionExpired(() => {
      dispatch(sitzungBeendet());
      navigate("/", { replace: true });
    });
  }, [dispatch, navigate]);

  // 2. Wenn der Status auf 'anonymous' wechselt (oder nach Docker-Neubau als
  // anonymous aufgelöst wird) und ein geschützter Pfad offen ist:
  // automatisch auf die Startseite navigieren, statt UI mit Fehlern stehenzulassen.
  useEffect(() => {
    const warAngemeldet = vorigerStatus.current === "authenticated";
    vorigerStatus.current = status;

    if (status !== "anonymous") return;

    // WER SEINE SITZUNG ABSICHTLICH BEENDET, WILL NICHT WEGGERISSEN WERDEN.
    //
    // Die Löschseite leert die Sitzung selbst und zeigt danach „angenommen und
    // läuft" — die einzige Auskunft, die es zu diesem Vorgang je gibt. Diese
    // Regel navigierte sie weg, und die Person sah stattdessen die
    // Marketingseite. Gemessen am 09.09.2026: `erasure-journey` fand die
    // Bestätigung nicht mehr.
    //
    // CLAUDE.md warnt an derselben Stelle vor der verwandten Falle („der
    // angenommene Zustand muss Vorrang vor der Anmeldeaufforderung haben") —
    // das hier ist dieselbe Falle, eine Ebene höher.
    if (location.pathname === "/delete-account") return;

    if (warAngemeldet || istGeschuetzterPfad(location.pathname)) {
      navigate("/", { replace: true });
    }
  }, [status, location.pathname, navigate]);

  useEffect(() => {
    if (status === "unknown") void dispatch(loadSession());
  }, [dispatch, status]);

  useEffect(() => {
    function pruefe() {
      if (document.visibilityState === "hidden") return;
      if (status === "unknown") return;
      void dispatch(loadSession());
    }

    document.addEventListener("visibilitychange", pruefe);
    window.addEventListener("focus", pruefe);
    return () => {
      document.removeEventListener("visibilitychange", pruefe);
      window.removeEventListener("focus", pruefe);
    };
  }, [dispatch, status]);

  useEffect(() => {
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
    if (status !== "authenticated") return;

    timer.current = setTimeout(() => {
      void dispatch(loadSession());
    }, ERNEUERUNG_NACH_MS);

    return () => {
      if (timer.current !== null) clearTimeout(timer.current);
    };
  }, [dispatch, status]);

  return <>{children}</>;
}
