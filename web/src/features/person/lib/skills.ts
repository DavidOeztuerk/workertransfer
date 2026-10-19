/**
 * Skills as form input.
 *
 * Comma-separated instead of chips: one line that can be pasted from a CV.
 * Empty entries and whitespace fall away — the domain additionally
 * deduplicates what slips through here.
 *
 * <strong>It rejects nothing and invents nothing.</strong> The vocabulary
 * (ADR-0023) renames, it never excludes: what the server does not know stays
 * exactly as it was typed. A list of permitted skills would be a claim about
 * which work exists.
 */
export function parseSkills(raw: string): string[] {
  return raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
