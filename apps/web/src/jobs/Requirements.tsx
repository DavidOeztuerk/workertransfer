import { VisuallyHidden } from "@workertransfer/ui";

import { matchSkills } from "./match";

/**
 * Was die Stelle verlangt — und, wenn ein Profil da ist, was davon man hat.
 *
 * Die Passung wird der PERSON gezeigt, nicht dem Unternehmen, und sie ordnet
 * Stellen, keine Menschen. Gerechnet wird hier im Browser: so gibt es sie
 * nirgends als Datensatz, den später jemand auswertet.
 *
 * Kein Prozentwert. Eine Zahl sieht aus wie eine Messung und verschweigt, was
 * zählt — welche Fähigkeit fehlt. Die Liste sagt es, und damit weiß die Person,
 * was sie tun könnte.
 *
 * Liegt hier und nicht in einer Route, weil sie auf ZWEI Seiten steht: in der
 * Trefferliste hilft sie beim Aussortieren, auf der Bewerbungsseite beim
 * Formulieren — dort sieht man, welche Fähigkeit fehlt, während man das
 * Anschreiben tippt. Nicht in `packages/ui`: eine Passung ist Produktlogik und
 * kein portables Primitiv (ADR-0022 gilt für sie ausdrücklich).
 */
export function Requirements({ skills, mine }: { skills: string[]; mine: string[] | null }) {
  // Dieselbe Aufbereitung wie im Abgleich, damit die angezeigte Liste und die
  // verglichene dieselbe ist. Liefen sie auseinander, stünde ein Eintrag da,
  // der nie ein Haken werden kann.
  const listed = skills.map((skill) => skill.trim()).filter((skill) => skill !== "");

  // Nichts genannt: dann gibt es auch nichts abzugleichen. Ein „0 von 0" wäre
  // eine Aussage über eine Stelle, die gar keine gemacht hat.
  if (listed.length === 0) return null;

  const match = mine === null || mine.length === 0 ? null : matchSkills(listed, mine);
  const have = new Set(match?.have ?? []);

  return (
    <div className="jobs__skills">
      {match !== null ? (
        <p className="candidates__meta">
          Du hast {match.have.length} von {listed.length} genannten Fähigkeiten:
        </p>
      ) : null}
      {mine !== null && mine.length === 0 ? (
        // Nicht „0 von 3": die Person hat nichts gesagt, nicht nichts gekonnt.
        <p className="candidates__meta">
          Trage Fähigkeiten in deinem <a href="/profile">Profil</a> ein, dann siehst du hier, was
          davon du mitbringst.
        </p>
      ) : null}
      <ul className="candidates__skills">
        {listed.map((skill) => {
          const state = match === null ? "unknown" : have.has(skill) ? "have" : "missing";
          return (
            <li key={skill}>
              {state !== "unknown" ? (
                <span aria-hidden="true">{state === "have" ? "✓ " : "✗ "}</span>
              ) : null}
              <span data-match={state}>{skill}</span>
              {/* Das Zeichen ist Dekoration; wer vorgelesen bekommt, braucht
                  das Wort. Sonst hörte man drei Namen und keinen Unterschied. */}
              {state !== "unknown" ? (
                <VisuallyHidden>{state === "have" ? " (hast du)" : " (fehlt dir)"}</VisuallyHidden>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
