// Server state for "is anyone signed in". The question goes to the public
// GET /auth/session, not to the protected /me: /me means "give me my profile"
// and answers 401 without credentials — correct for a protected resource, but
// it turned every page view by a signed-out visitor into an error entry about
// a perfectly normal state. `null` means anonymous, and an anonymous visitor
// causes exactly ONE request (see fetchSession).

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { type MeResponse, fetchSession, logout } from "./client";

export const SESSION_QUERY_KEY = ["session"] as const;

export interface Session {
  readonly user: MeResponse | null;
  readonly isLoading: boolean;
}

export function useSession(): Session {
  const { data, isPending } = useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: fetchSession,
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
