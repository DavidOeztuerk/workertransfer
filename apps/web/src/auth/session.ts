// Server state for "who am I". GET /me authorises via the httpOnly `access`
// cookie, so the browser never holds the token itself — the query result *is*
// the session. `null` means anonymous (a 401 is a state, not an error), which
// is why fetchMe resolves rather than throws.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { type MeResponse, fetchMe, logout } from "./client";

export const SESSION_QUERY_KEY = ["session"] as const;

export interface Session {
  readonly user: MeResponse | null;
  readonly isLoading: boolean;
}

export function useSession(): Session {
  const { data, isPending } = useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: fetchMe,
    staleTime: 30_000,
  });
  return { user: data ?? null, isLoading: isPending };
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: logout,
    onSettled: async () => {
      queryClient.setQueryData(SESSION_QUERY_KEY, null);
      await queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
    },
  });
}
