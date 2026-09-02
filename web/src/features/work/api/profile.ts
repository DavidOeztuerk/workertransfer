// Nur das eine Stück profile-service, das dieser Bereich braucht: die eigenen
// Fähigkeiten.
//
// Der Abgleich mit einer Stelle passiert im Browser (`lib/match.ts`), und dafür
// muss die Seite wissen, was die Person eingetragen hat. Mehr wird hier nicht
// gelesen und nichts geschrieben — das Profil selbst gehört `features/person`.

import { request } from "../../../core/api/client";
import { PROFILE_BASE_URL } from "../../../env";

export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  updated_at: string;
}

/**
 * Das eigene Profil, oder `null`.
 *
 * `null` heißt „noch keins angelegt" ODER „nicht abrufbar". Für die Passung ist
 * beides derselbe Fall — und der wichtige Unterschied liegt eine Ebene höher:
 * `null` heißt „nichts zu vergleichen" und schweigt, ein Profil MIT leerer
 * Fähigkeitsliste heißt „nichts eingetragen" und bekommt einen Satz. Nie ein
 * „0 von 3": die Person hat nichts gesagt, nicht nichts gekonnt.
 */
export async function getMyProfile(signal?: AbortSignal): Promise<Profile | null> {
  const antwort = await request<Profile | null>(PROFILE_BASE_URL, "/profiles/me", { signal });
  return antwort.ok ? (antwort.value ?? null) : null;
}
