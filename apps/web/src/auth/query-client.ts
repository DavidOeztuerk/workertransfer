// Single shared QueryClient. Built here (not inline in main.tsx) so tests and
// the router can import the same instance and default options are declared once.
// retry:false — auth/network failures should surface to the user, not silently
// retry in the background; refetchOnWindowFocus:false keeps /me from hammering
// the service on every tab refocus in dev.

import { QueryClient } from "@tanstack/react-query";

export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
});
