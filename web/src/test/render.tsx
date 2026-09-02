// Test render helper: components that read server state (the session) need a
// QueryClientProvider. Each call gets a fresh QueryClient so no cache leaks
// between tests, and retries are off so a failed query surfaces immediately
// instead of stalling the test for three backoff rounds.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { type RenderOptions, render } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: 0 },
      mutations: { retry: false },
    },
  });
}

export function renderWithProviders(
  ui: ReactElement,
  options: RenderOptions & { queryClient?: QueryClient } = {}
) {
  const { queryClient = createTestQueryClient(), ...renderOptions } = options;
  function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  }
  return { queryClient, ...render(ui, { wrapper: Wrapper, ...renderOptions }) };
}
