/**
 * Tells the other tabs of this browser that the session is over.
 *
 * All tabs share the httpOnly cookies, so a sign-out in one tab ends the
 * session for all of them on the server — but the others only notice at their
 * next request and keep showing a signed-in page until then. A
 * `BroadcastChannel` reaches them at once.
 *
 * Carries no token and no name: only that the session ended.
 */

const CHANNEL = "workertransfer-session";

/**
 * Who sent it. A `BroadcastChannel` delivers to every OTHER channel object —
 * including the listener in this very tab. Without the id the tab that signed
 * out heard itself and navigated away: the erasure page lost its "läuft"
 * before anyone could read it (measured 07.10.2026).
 */
const THIS_TAB = crypto.randomUUID();

type Message = { type: "signed-out"; from: string };

function open(): BroadcastChannel | null {
  return typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel(CHANNEL);
}

/** Tells every other tab that this session ended. */
export function announceSignedOut(): void {
  const channel = open();
  if (channel === null) return;
  channel.postMessage({ type: "signed-out", from: THIS_TAB } satisfies Message);
  channel.close();
}

/** Calls `onSignedOut` when another tab ends the session; returns the unsubscribe. */
export function listenForSignOut(onSignedOut: () => void): () => void {
  const channel = open();
  if (channel === null) return () => undefined;

  channel.onmessage = (event: MessageEvent<Message>) => {
    if (event.data?.type === "signed-out" && event.data.from !== THIS_TAB) onSignedOut();
  };

  return () => channel.close();
}
