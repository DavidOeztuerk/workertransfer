import { describe, expect, it } from "vitest";

import * as env from "./env";

/**
 * Every base URL points at its service's port.
 *
 * The occasion: `GITHUB_BASE_URL` fell back to **8010** — the port of
 * notification-service. Whoever opened `/github` sent their account name to
 * the wrong service and got an error message that looked like a refused
 * account.
 *
 * It went unnoticed because nobody misses the neighbour: the UI does not talk
 * to notification-service at all (the notification settings live at
 * identity). An offset by one therefore hit a single caller only, and that one
 * only on a page no unit test opens.
 *
 * The table is written out here and not derived from the code — otherwise it
 * would check itself. It is the same as in CLAUDE.md and in
 * `docker-compose.yml`; a new service belongs in all three places.
 */
const PORTS: [string, number][] = [
  ["API_BASE_URL", 8001],
  ["CONSENT_BASE_URL", 8002],
  ["PROFILE_BASE_URL", 8003],
  ["RESUME_BASE_URL", 8004],
  ["PORTFOLIO_BASE_URL", 8005],
  ["JOBS_BASE_URL", 8006],
  ["APPLICATIONS_BASE_URL", 8007],
  ["COMPANIES_BASE_URL", 8008],
  ["TRANSFER_BASE_URL", 8009],
  ["GITHUB_BASE_URL", 8011],
];

describe("the base URLs", () => {
  it.each(PORTS)("%s points at %i", (name, port) => {
    expect(env[name as keyof typeof env]).toBe(`http://localhost:${port}`);
  });

  /**
   * Two services must not share a port — a typo would otherwise be not "wrong
   * port" but "two callers, one target", and that shows up even later.
   */
  it("assigns no port twice", () => {
    const urls = PORTS.map(([name]) => env[name as keyof typeof env]);

    expect(new Set(urls).size).toBe(urls.length);
  });
});
