// Skills as form input — for a person's profile and for what a job asks for.
//
// One parser for both sides, because both lists are held against each other in
// the browser (`lib/match.ts`). Two parsers that diverge would find accidental
// hits: if one side wrote `"Python, Go"` as two entries and the other as one,
// sentences would afterwards be compared with words.
//
// <strong>It renames nothing and excludes nothing.</strong> Unifying spellings
// ("Postgres" = "PostgreSQL") happens in the service, in the skill value
// objects (ADR-0023) — and there it is a statement about LANGUAGE. A list of
// permitted skills in the browser would be a claim about which work exists.

/**
 * Comma-separated instead of chips: one line that can be pasted from a CV.
 * Empty entries and whitespace fall away — the domain additionally
 * de-duplicates what slips through here.
 */
export function parseSkills(raw: string): string[] {
  return raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
