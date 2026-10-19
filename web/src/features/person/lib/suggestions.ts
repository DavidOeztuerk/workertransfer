import type { Repository } from "../api/github";

/**
 * How many suggestions are offered at most.
 *
 * Whoever has a hundred repositories quickly has three hundred different
 * topics. Nobody reads a wall of words, and whoever adopts them all anyway
 * claims something about themselves they did not mean.
 */
const MAX_SUGGESTIONS = 24;

/** One source: the words of a repository, a CV position, a portfolio item. */
export interface Source {
  words: readonly string[];
  /**
   * Did the person write these words THEMSELVES?
   *
   * The technologies of a CV position, yes; the topics of a repository too,
   * but on an artefact rather than about themselves. The difference decides
   * the order — see <c>suggestionsFrom</c>.
   */
  selfTyped: boolean;
}

/**
 * Evidence becomes SUGGESTIONS — never namings.
 *
 * <strong>This is the seam ADR-0022 hangs on.</strong> What occurs in a
 * repository is a fact about a repository. That a person can do something is
 * a statement about a human, and only they may make it. This function does
 * not bridge the two — it only lays out words that a human adopts with a click
 * or leaves standing.
 *
 * That is why it writes nothing either: it fills the form field, and that
 * holds only with "Save". Two acts, so that neither happens by accident.
 *
 * <strong>Self-typed comes first, and that was measured.</strong> Without this
 * step frequency alone decided the order — and an account with a hundred
 * repositories pushed the three technologies from one's own CV out of the top
 * twenty-four. Measured on a real account: "Kafka" stood in two positions,
 * "nodejs" in dozens of repositories, and one's own statement could no longer
 * be seen. A word somebody wrote about THEIR WORK weighs more than one found on
 * an artefact — the same precedence as "named before evidenced".
 *
 * <strong>The order ranks WORDS, never people.</strong> No number from it is
 * shown anywhere, and no ranking of anybody follows from the order.
 *
 * @param sources One source per repository, CV position or portfolio item.
 * @param alreadyNamed What the person has already typed into the profile themselves.
 * @returns Words they have not named yet — the closest first.
 */
export function suggestionsFrom(
  sources: readonly Source[],
  alreadyNamed: readonly string[]
): string[] {
  // The comparison ignores case: whoever has "TypeScript" in the profile
  // should not be offered "typescript". The vocabulary (ADR-0023) unifies only
  // when the service saves.
  const known = new Set(alreadyNamed.map((word) => word.trim().toLowerCase()));

  const found = new Map<
    string,
    { word: string; sources: number; selfTyped: boolean }
  >();

  for (const source of sources) {
    // Count once per SOURCE, even if a word stands there several times — for
    // example as topic and language of the same repository. Otherwise the
    // spelling would count, not the project.
    const words = new Set(
      source.words.map((word) => word.trim()).filter((word) => word.length > 0)
    );

    for (const word of words) {
      const key = word.toLowerCase();
      if (known.has(key)) continue;

      const entry = found.get(key);

      if (entry === undefined) {
        found.set(key, {
          word,
          sources: 1,
          selfTyped: source.selfTyped,
        });
      } else {
        entry.sources += 1;
        // Once self-typed stays self-typed: that the same word also sticks to
        // a repository does not make one's own statement weaker.
        entry.selfTyped ||= source.selfTyped;
      }
    }
  }

  return [...found.values()]
    .sort(
      (a, b) =>
        Number(b.selfTyped) - Number(a.selfTyped) ||
        b.sources - a.sources ||
        a.word.localeCompare(b.word)
    )
    .slice(0, MAX_SUGGESTIONS)
    .map((entry) => entry.word);
}

/**
 * Repositories as sources.
 *
 * <c>selfTyped: false</c> — topics and languages stick to an artefact. The
 * owner wrote them, but about a project, not about themselves.
 */
export function fromRepositories(
  repositories: readonly Repository[]
): Source[] {
  return repositories.map((repository) => ({
    words: [...repository.topics, ...repository.languages],
    selfTyped: false,
  }));
}

/**
 * Portfolio items as sources.
 *
 * <c>selfTyped: true</c> — "I built this with that" is a sentence about one's
 * own work, just as at a CV position.
 */
export function fromPortfolioItems(
  items: readonly { technologies: string[] }[]
): Source[] {
  return items.map((item) => ({
    words: item.technologies,
    selfTyped: true,
  }));
}

/**
 * CV positions as sources.
 *
 * <c>selfTyped: true</c> — "I worked there with that" is a sentence about
 * one's own work, not about an artefact.
 */
export function fromPositions(
  positions: readonly { technologies: string[] }[]
): Source[] {
  return positions.map((position) => ({
    words: position.technologies,
    selfTyped: true,
  }));
}

/**
 * One's own documents as sources — the fourth.
 *
 * <c>selfTyped: false</c>, and that is a decision: a job reference was written
 * by the employer, not by the person. It therefore stands on the same step as
 * a GitHub topic — evidence on an <em>artefact</em>, behind what somebody said
 * about their own work. That a paper master craftsman's certificate weighs more
 * than a repository is true and none of this platform's business: the moment
 * it started weighing evidence against evidence it would have arrived at the
 * number from ADR-0022 (ADR-0039).
 *
 * The words are canonical names from the vocabulary — it renames and never
 * infers. And from here too no path leads past the two acts: a click fills the
 * field, only "Save" makes it a naming.
 */
export function fromDocuments(
  findings: readonly { terms: string[] }[]
): Source[] {
  return findings.map((finding) => ({
    words: finding.terms,
    selfTyped: false,
  }));
}
