// The search for people — scout-service, no longer profile-service.
//
// <strong>`GET /scout/candidates`.</strong> The predecessor was
// `GET /candidates` at profile-service; it fell with this file on 11.09.2026
// (ADR-0036). Two searches side by side would have been two truths.
//
// <strong>The ticks and the evidence come FROM THE SERVER</strong>, not from a
// calculation here. That is decision 3 of the ADR and no matter of taste: if
// the list lay only in the UI, the browser would compute a number from the raw
// data — and ADR-0022 would be back through the back door.

import { request } from "../../../core/api/client";
import { SCOUT_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";

/** A tick: a searched word and a yes or no. */
export interface Tick {
  word: string;
  /** Whether the person named this word themselves. */
  named: boolean;
}

/**
 * A piece of evidence with origin and link.
 *
 * `origin` is `topic` or `language`: a topic was written onto the repository by
 * a human, a language name was recognised by GitHub. Without the statement both
 * would read like a statement about the person.
 */
export interface Evidence {
  word: string;
  origin: "topic" | "language";
  project: string;
  url: string;
}

/**
 * What the server says about the completeness of the evidence.
 *
 * A word and not a sentence — the UI phrases it in the reading person's
 * language (ADR-0031). `none_released` and `unavailable` are explicitly
 * different: "nothing stands here" is no judgement, "we do not know right now"
 * is no statement about the person at all.
 */
export type EvidenceState = "complete" | "none_released" | "partial" | "unavailable";

/**
 * The tick for the distance (ADR-0041).
 *
 * `unsaid` is NOT a no. It means: the person said nothing, or no job was
 * chosen, or a place is unknown to the server. Whoever said nothing gets no
 * cross.
 */
export type AvailabilityState = "reachable" | "further" | "unsaid";

/** A person who was found. Without a score, without a rank, without a percentage. */
export interface Hit {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  /** What the person named themselves. */
  named: string[];
  /** One tick per searched word — never a sum over them. */
  checks: Tick[];
  evidence: Evidence[];
  evidence_state: EvidenceState;
  /** Three words, never a kilometre figure — that does not even travel along. */
  reach: AvailabilityState;
  /** What the person said, or `null`. */
  reach_commute: string | null;
  /** What the job demands, or `null`. */
  reach_attendance: string | null;
}

export interface SearchFilter {
  /**
   * Joined with OR, and that is the reason the ticks say something: under AND
   * every hit would satisfy everything searched, every tick would be set, and
   * "which skill is missing" would have no answer.
   */
  skills: string[];
  location: string;
  /** Only `true` filters. There is no "only without remote" — see the server. */
  remoteOnly: boolean;
}

export const NO_FILTER: SearchFilter = { skills: [], location: "", remoteOnly: false };

export type ScoutError =
  | "no-company"
  | "unauthenticated"
  | "consent-unavailable"
  | "offline"
  | "advert-gone"
  | "advert-without-skills"
  | "failed";

export type HitPage =
  | { ok: true; items: Hit[]; nextCursor: string | null }
  | Failure<ScoutError>;

/**
 * The filters travel into the URL, not into the cursor.
 *
 * A cursor that packs search conditions is a second place where the query
 * stands — and the first time both drift apart, someone silently pages through
 * the wrong set.
 */
export function scoutQuery(
  cursor?: string,
  filter: SearchFilter = NO_FILTER,
  jobId?: string
): string {
  const params = new URLSearchParams();
  if (cursor !== undefined && cursor !== "") params.set("cursor", cursor);
  // The job DOES NOT FILTER (ADR-0041): it adds a statement to every hit and
  // takes none away. That is why it stands beside the filters and not among
  // them. The query key `stelle` is the wire contract with scout-service.
  if (jobId !== undefined && jobId !== "") params.set("stelle", jobId);
  for (const skill of filter.skills) {
    const trimmed = skill.trim();
    if (trimmed !== "") params.append("skill", trimmed);
  }
  if (filter.location.trim() !== "") params.set("location", filter.location.trim());
  // `remote=false` is not even sent: it would be no filter but noise in the
  // URL.
  if (filter.remoteOnly) params.set("remote", "true");
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}

/**
 * A page of hits — for companies only.
 *
 * <strong>`next` and not `next_cursor`.</strong> The predecessor read
 * `next_cursor`, the server wrote `next`: the cursor was therefore ALWAYS
 * `null`, the "load more" button never appeared, and the candidate list had no
 * way to page 2. Measured and noted as an open gap in
 * `SCOUT-UND-BERATER-BESTAND.md` — the cause was a field name.
 *
 * The reasons can be told apart because the page answers each of them
 * differently: "no company active" can be fixed (switch), "ledger silent"
 * cannot, and an empty page is no error at all.
 */
export async function searchCandidates(
  cursor?: string,
  filter: SearchFilter = NO_FILTER,
  signal?: AbortSignal,
  jobId?: string
): Promise<HitPage> {
  const answer = await request<{ items?: Hit[]; next?: string | null }>(
    SCOUT_BASE_URL,
    `/scout/candidates${scoutQuery(cursor, filter, jobId)}`,
    { signal },
    "errors.listNotLoaded2"
  );
  if (answer.ok) {
    return {
      ok: true,
      items: answer.value?.items ?? [],
      nextCursor: answer.value?.next ?? null,
    };
  }
  return interpretError<ScoutError>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      401: {
        reason: "unauthenticated",
        titleKey: "errors.sessionExpired",
        detailKey: "errors.signInAgain",
      },
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesSeeProfiles",
        detailKey: "errors.chooseCompany",
      },
      // NOT shown as an empty list: that would be the claim that nobody
      // released anything — and exactly that nobody knows at this moment.
      503: {
        reason: "consent-unavailable",
        titleKey: "errors.ledgerSilent",
        detailKey: "errors.tryLater",
      },
    },
    "offline"
  );
}

/**
 * "Who fits this advert?" — the scout per advert (VISION-FRAGEN B1).
 *
 * The same tick list as the free search, with the advert's skills as the
 * words and its place as the commute tick. No ranking, no total; the ledger
 * is asked on every load. It runs when somebody opens it — nothing is stored.
 */
export async function advertCandidates(
  jobId: string,
  cursor?: string,
  signal?: AbortSignal
): Promise<HitPage> {
  const params = new URLSearchParams();
  if (cursor !== undefined && cursor !== "") params.set("cursor", cursor);
  const query = params.toString();
  const answer = await request<{ items?: Hit[]; next?: string | null }>(
    SCOUT_BASE_URL,
    `/scout/jobs/${jobId}/candidates${query === "" ? "" : `?${query}`}`,
    { signal },
    "errors.listNotLoaded2"
  );
  if (answer.ok) {
    return { ok: true, items: answer.value?.items ?? [], nextCursor: answer.value?.next ?? null };
  }
  return interpretError<ScoutError>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesSeeProfiles",
        detailKey: "errors.chooseCompany",
      },
      // detailKey too: the page draws `failureKey`, which reads the detail key.
      404: { reason: "advert-gone", titleKey: "errors.advertGone", detailKey: "errors.advertGone" },
      422: {
        reason: "advert-without-skills",
        titleKey: "errors.advertNoSkills",
        detailKey: "errors.advertNoSkills",
      },
      503: { reason: "consent-unavailable", titleKey: "errors.serviceSilent", detailKey: "errors.serviceSilent" },
    },
    "failed"
  );
}
