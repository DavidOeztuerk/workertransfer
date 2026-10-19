import { useAppSelector } from "../../../core/store/hooks";
import type { Session, SessionStatus } from "../../auth/types/session";

/*
 * Twin of `features/work/lib/session.ts`. It belonged in `src/shared/hooks/` —
 * that was foreign territory in this pass. When the streams are merged: move it
 * there, delete both copies.
 */


/**
 * Who is acting right now — readable for a page.
 *
 * <strong>Three states, not two.</strong> `status === "unknown"` means "not
 * asked yet" and must be drawn as a LOADING state. If a page draws it as
 * "signed out", every page briefly flashes "Please sign in" on a cold start
 * although the person is signed in. The old pages did not know this case: they
 * got `principal` injected as a prop, and it was either there or `null`.
 *
 * `tenantId === null` means "acting as a person", not "missing" (ADR-0017). On
 * a transfer market that is the normal case.
 */
export interface Actor {
  status: SessionStatus;
  session: Session | null;
  /** The session is still unknown — draw a loading state. */
  loading: boolean;
  signedIn: boolean;
  /** One's own subject id, or `null`. */
  subjectId: string | null;
  /** The company being acted for right now, or `null`. */
  tenantId: string | null;
  /** Acting for a company right now. */
  forCompany: boolean;
}

export function useActor(): Actor {
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);
  const signedIn = status === "authenticated" && session !== null;

  return {
    status,
    session,
    loading: status === "unknown",
    signedIn,
    subjectId: signedIn ? session.userId : null,
    tenantId: signedIn ? session.tenantId : null,
    forCompany: signedIn && session.tenantId !== null,
  };
}
