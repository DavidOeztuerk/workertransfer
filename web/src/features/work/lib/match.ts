// The comparison between one's own profile and what a job asks for.
//
// It stands here and not on the server, and that is the load-bearing decision
// of this cut: **the fit exists nowhere as a record.** No field, no table, no
// figure that somebody evaluates later or shows to a company. It lives while
// the page is open and disappears with it — what does not exist cannot be
// evaluated either.
//
// And it goes in the other direction from the usual "matching": it orders jobs
// for a person, not people for a company. A ranking of people would stay a
// ranking of people, whatever it was computed from (ADR-0022).

export interface SkillMatch {
  /** From the job's list: what the person can do according to the profile. */
  have: string[];
  /** From the same list: what is missing. That is the part that helps. */
  missing: string[];
}

// `toLowerCase` instead of `toLocaleLowerCase`: the latter turns "I" into "ı" in
// Turkish, and the browser's language would then influence whether somebody
// counts as a fit.
const key = (skill: string): string => skill.trim().toLowerCase();

/**
 * Splits the job's list into ticks and gaps.
 *
 * Deliberately **no** percentage: one looks like a measurement and is a
 * division — and it hides exactly what counts, namely WHICH skill is missing.
 * The list says it, and with it the person knows what they could do.
 *
 * Compared for equality, not containment: "Java" is not "JavaScript", and a
 * hit that nobody claimed sends somebody into a conversation with false
 * information.
 */
export function matchSkills(jobSkills: string[], profileSkills: string[]): SkillMatch {
  const owned = new Set(profileSkills.map(key).filter((skill) => skill !== ""));
  const have: string[] = [];
  const missing: string[] = [];

  // Order and spelling are those of the advert. Overwriting them with the
  // person's words would silently reinterpret what the company wrote.
  for (const skill of jobSkills) {
    const cleaned = skill.trim();
    if (cleaned === "") continue;
    (owned.has(key(cleaned)) ? have : missing).push(cleaned);
  }

  return { have, missing };
}
