import { useEffect, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { onSessionExpired } from "../../../core/api/client";
import { sitzungBeendet } from "../../../features/auth/store/authSlice";
import { loadSession } from "../../../features/auth/store/authThunks";

/**
 * The access JWT lives fifteen minutes (Girder `ExpireMinutes`). Two minutes of
 * buffer, because the cookie is httpOnly and the UI cannot read `exp`.
 * Visibility/focus catches the laptop lid.
 */
const RENEW_AFTER_MS = 13 * 60 * 1000;

const PUBLIC_PATHS = [
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

function isProtectedPath(pathname: string): boolean {
  if (pathname.startsWith("/careers/")) return false;
  return !PUBLIC_PATHS.some((open) => pathname === open);
}

/**
 * Asks who is acting right now — and keeps the session alive.
 *
 * When the token has expired (e.g. after a Docker rebuild or an expiry without
 * renewal), the session falls at once and the user is navigated cleanly to the
 * start page instead of lingering on a protected page with error messages.
 *
 * <strong>It does not block the page.</strong> A loading screen over the whole
 * application, only to learn whether somebody is signed in, makes every cold
 * start slow — the marketing page too, which needs to know nobody.
 *
 * Tokens stay in the httpOnly cookie. No JWT is decoded here and nothing is put
 * into `localStorage`.
 */
export function SessionGate({ children }: { children: React.ReactNode }) {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const location = useLocation();
  const status = useAppSelector((state) => state.auth.status);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const previousStatus = useRef(status);

  // 1. When a call finds that token and refresh are dead: end the session at
  // once and navigate to the start page.
  useEffect(() => {
    return onSessionExpired(() => {
      dispatch(sitzungBeendet());
      navigate("/", { replace: true });
    });
  }, [dispatch, navigate]);

  // 2. When the status changes to 'anonymous' (or resolves as anonymous after a
  // Docker rebuild) and a protected path is open: navigate to the start page
  // automatically instead of leaving a UI full of errors standing.
  useEffect(() => {
    const wasSignedIn = previousStatus.current === "authenticated";
    previousStatus.current = status;

    if (status !== "anonymous") return;

    // WHOEVER ENDS THEIR SESSION ON PURPOSE DOES NOT WANT TO BE TORN AWAY.
    //
    // The erasure page clears the session itself and then shows "accepted and
    // running" — the only information about this case there will ever be.
    // This rule navigated it away, and the person saw the marketing page
    // instead. Measured on 09.09.2026: `erasure-journey` no longer found the
    // confirmation.
    //
    // CLAUDE.md warns of the related trap at the same place ("the accepted
    // state must take precedence over the login prompt") — this is the same
    // trap, one level up.
    if (location.pathname === "/delete-account") return;

    if (wasSignedIn || isProtectedPath(location.pathname)) {
      navigate("/", { replace: true });
    }
  }, [status, location.pathname, navigate]);

  useEffect(() => {
    if (status === "unknown") void dispatch(loadSession());
  }, [dispatch, status]);

  useEffect(() => {
    function check() {
      if (document.visibilityState === "hidden") return;
      if (status === "unknown") return;
      void dispatch(loadSession());
    }

    document.addEventListener("visibilitychange", check);
    window.addEventListener("focus", check);
    return () => {
      document.removeEventListener("visibilitychange", check);
      window.removeEventListener("focus", check);
    };
  }, [dispatch, status]);

  useEffect(() => {
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
    if (status !== "authenticated") return;

    timer.current = setTimeout(() => {
      void dispatch(loadSession());
    }, RENEW_AFTER_MS);

    return () => {
      if (timer.current !== null) clearTimeout(timer.current);
    };
  }, [dispatch, status]);

  return <>{children}</>;
}
