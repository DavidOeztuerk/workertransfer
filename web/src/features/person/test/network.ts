import { vi } from "vitest";

/**
 * A stand-in for `fetch` — and the reason it stands here instead of a
 * `vi.mock()` line over the API modules.
 *
 * The old tests replaced the client modules. A test then checks the page's
 * behaviour but never the <strong>wire</strong>: which path it calls, with
 * which method, and with which field names in the body. Exactly there hid the
 * costliest defect of this migration — the UI sent `remote_ok`, the server
 * bound `remoteOk`, and the "remote possible" tick was silently lost on save.
 * Not a single test could see it.
 *
 * So the stand-in sits one level lower: the real API modules run along, and
 * `calls` records what actually went out.
 */
export interface Call {
  method: string;
  /** Only the path, without origin — the base URL depends on the environment. */
  path: string;
  /** The path including the query, for tests that check the filters. */
  url: string;
  /** The body sent, already parsed. `undefined` if there was none. */
  body: unknown;
}

export interface StubResponse {
  status?: number;
  body?: unknown;
}

type Reaction = StubResponse | ((call: Call) => StubResponse | Promise<StubResponse>);

/** Keys are `"<METHOD> <path>"`, for example `"GET /profiles/me"`. */
export type Reactions = Record<string, Reaction>;

export interface Network {
  /** Everything that went out — in the order of the calls. */
  calls: Call[];
  /** The last call on this key, or `undefined`. */
  last: (key: string) => Call | undefined;
  /** Change a reaction afterwards (for the second click, say). */
  set: (key: string, reaction: Reaction) => void;
}

function pathOf(input: string): string {
  // An absolute URL (the normal case — `request()` puts the base in front) or,
  // if need be, already a bare path.
  try {
    return new URL(input).pathname + new URL(input).search;
  } catch {
    return input;
  }
}

/**
 * Hangs a stand-in on `globalThis.fetch` and returns the record.
 *
 * A call without a matching reaction <strong>throws</strong>, with the key it
 * looked for in the message. A silent 404 would be worse: the page would then
 * show "nothing there", and the test would be green for the wrong reason —
 * exactly the confusion this whole area is built against.
 */
export function stubNetwork(reactions: Reactions = {}): Network {
  const table = new Map<string, Reaction>(Object.entries(reactions));
  const calls: Call[] = [];

  vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit) => {
    const raw = typeof input === "string" ? input : input.toString();
    const url = pathOf(raw);
    const path = url.split("?")[0] ?? url;
    const method = (init?.method ?? "GET").toUpperCase();

    let body: unknown;
    if (typeof init?.body === "string") {
      try {
        body = JSON.parse(init.body);
      } catch {
        body = init.body;
      }
    } else if (init?.body !== undefined && init.body !== null) {
      // FormData and the like: passed through unchanged so that a test can
      // look at the file.
      body = init.body;
    }

    const call: Call = { method, path, url, body };
    calls.push(call);

    const reaction = table.get(`${method} ${path}`);
    if (reaction === undefined) {
      throw new Error(
        `No entry in the network stub for "${method} ${path}". ` +
          `Present: ${[...table.keys()].join(", ") || "(none)"}`
      );
    }

    // `responseBody`, because `body` already belongs to the REQUEST in this
    // function.
    const { status = 200, body: responseBody } =
      typeof reaction === "function" ? await reaction(call) : reaction;

    return new Response(
      responseBody === undefined ? null : JSON.stringify(responseBody),
      {
        status,
        headers:
          responseBody === undefined
            ? undefined
            : { "content-type": "application/json" },
      }
    );
  });

  return {
    calls,
    last: (key) => {
      const [method, path] = key.split(" ");
      return [...calls].reverse().find((c) => c.method === method && c.path === path);
    },
    set: (key, reaction) => table.set(key, reaction),
  };
}
