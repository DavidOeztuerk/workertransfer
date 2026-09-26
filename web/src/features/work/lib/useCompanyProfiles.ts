import { useEffect, useRef, useState } from "react";

import { type CompanyProfile, getCompanyProfile } from "../../company/api/companies";

/**
 * The company profiles for a list of jobs — asked once per company, not per job.
 *
 * Several jobs of the same employer regularly stand side by side. One request
 * per card would mean fetching the same answer five times; in the old code that
 * was handled by TanStack Query's query key, and that is missing here (no
 * `QueryClientProvider`, see `lib/useAsync.ts`).
 *
 * A missing entry means "not asked yet", an entry `null` means "has no
 * profile". The difference carries: in the first case the card shows nothing
 * because it does not know, in the second nothing because the company stays
 * anonymous — and never a placeholder nobody wrote.
 */
export function useCompanyProfiles(tenantIds: string[]): Record<string, CompanyProfile | null> {
  const [profiles, setProfiles] = useState<Record<string, CompanyProfile | null>>({});
  // Two mirrors beside the state: `loaded` holds what is already answered,
  // `running` what is on its way right now. Without both, every render would ask
  // again or an aborted request would never be made up.
  const loaded = useRef<Record<string, CompanyProfile | null>>({});
  const running = useRef<Set<string>>(new Set());

  // A string as the dependency: a fresh array would be a different one on
  // every render and would drive the effect endlessly.
  const key = [...new Set(tenantIds)].sort().join(",");

  useEffect(() => {
    const ids = key === "" ? [] : key.split(",");
    const open = ids.filter((id) => !(id in loaded.current) && !running.current.has(id));
    if (open.length === 0) return;
    for (const id of open) running.current.add(id);

    const abort = new AbortController();
    let alive = true;
    void Promise.all(
      open.map(async (id) => [id, await getCompanyProfile(id, abort.signal)] as const)
    ).then((pairs) => {
      if (!alive) return;
      for (const [id, value] of pairs) {
        loaded.current[id] = value;
        running.current.delete(id);
      }
      setProfiles({ ...loaded.current });
    });

    return () => {
      alive = false;
      abort.abort();
      // Aborted ones do NOT stay marked as "on their way" — otherwise the name
      // would be missing for the rest of the session, because nobody fetches it
      // again.
      for (const id of open) if (!(id in loaded.current)) running.current.delete(id);
    };
  }, [key]);

  return profiles;
}
