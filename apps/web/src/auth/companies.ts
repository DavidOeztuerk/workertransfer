// Server state for "which companies may I act for". The list comes from
// GET /me/companies; switching mints a new token pair server-side, so the
// session query must be invalidated afterwards — the tenant lives in the token,
// not in client state.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { type Membership, listCompanies, switchCompany } from "./client";
import { SESSION_QUERY_KEY } from "./session";

export const COMPANIES_QUERY_KEY = ["companies"] as const;

export function useCompanies(): { companies: Membership[]; isLoading: boolean } {
  const { data, isPending } = useQuery({
    queryKey: COMPANIES_QUERY_KEY,
    queryFn: listCompanies,
    staleTime: 30_000,
  });
  return { companies: data ?? [], isLoading: isPending };
}

export function useSwitchCompany() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: switchCompany,
    onSettled: async () => {
      // The new cookies carry a different tenant claim; anything derived from
      // the old one is stale.
      await queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
    },
  });
}
