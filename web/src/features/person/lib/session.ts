import { useAppSelector } from "../../../core/store/hooks";

/**
 * Who is acting right now — for the pages of this area.
 *
 * <strong>Three states, not two.</strong> `unknown` means "not asked yet" and
 * must be drawn as LOADING. Drawn as "signed out", every one of these pages
 * would briefly flash "Please sign in" although the person is signed in — and
 * on `/delete-account` that would be the worst sentence the application can
 * say.
 */
export function usePerson(): {
  subjectId: string | null;
  email: string | null;
  /** The session has not been checked yet. Neither signed in nor signed out. */
  sessionUnknown: boolean;
} {
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  return {
    subjectId: session?.userId ?? null,
    email: session?.email ?? null,
    sessionUnknown: status === "unknown",
  };
}
