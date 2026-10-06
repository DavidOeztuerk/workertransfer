import { afterEach, describe, expect, it, vi } from "vitest";

import { announceSignedOut, listenForSignOut } from "./sessionBroadcast";

/**
 * A sign-out in one tab reaches the others at once — they share the cookies,
 * so the session is over for them too, and until 06.10.2026 they only found out
 * at their next request.
 */
describe("the session broadcast", () => {
  const stops: Array<() => void> = [];

  afterEach(() => {
    while (stops.length > 0) stops.pop()?.();
  });

  it("tells another tab that the session ended", async () => {
    const other = vi.fn();
    stops.push(listenForSignOut(other));

    announceSignedOut();

    await vi.waitFor(() => expect(other).toHaveBeenCalledTimes(1));
  });

  it("ignores messages that are not a sign-out", async () => {
    const other = vi.fn();
    stops.push(listenForSignOut(other));

    const stranger = new BroadcastChannel("workertransfer-session");
    stranger.postMessage({ type: "something-else" });
    stranger.close();
    announceSignedOut();

    await vi.waitFor(() => expect(other).toHaveBeenCalledTimes(1));
  });
});
