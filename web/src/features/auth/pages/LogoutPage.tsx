import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Navigate } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { logout } from "../store/authThunks";

/**
 * Sign out — an address of its own and no button in the header.
 *
 * <strong>Why a page.</strong> Signing out clears a cookie the browser never
 * gets to see; only the server can. As an address the operation can be shared,
 * reached from a menu and — more important — it has a visible intermediate
 * state. A button that silently does nothing because the request hangs looks
 * like a broken menu.
 *
 * <strong>Exactly once.</strong> React 19 calls effects twice in StrictMode;
 * without the lock the sign-out command would run twice, and the second would
 * hit a session that no longer exists. That would be harmless, but it would
 * stand in the log as an error — and a log full of harmless errors is no longer
 * read.
 */
export function LogoutPage() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const status = useAppSelector((state) => state.auth.status);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void dispatch(logout());
  }, [dispatch]);

  // Only once the server has confirmed. Redirecting earlier would mean claiming
  // a sign-out that perhaps did not happen.
  if (status === "anonymous") {
    return <Navigate to="/" replace />;
  }

  return <LoadingBlock label={t("logout.inProgress")} />;
}
