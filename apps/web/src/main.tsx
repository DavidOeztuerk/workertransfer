import { QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { router } from "./app";
import { queryClient } from "./auth/query-client";
import "@workertransfer/ui/styles.css";
import "./styles.css";

const container = document.getElementById("root");

if (container === null) {
  throw new Error("WorkerTransfer could not find the application root.");
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>
);
