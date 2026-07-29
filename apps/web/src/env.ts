// Resolved base URL for the backend API. Vite injects import.meta.env.VITE_API_BASE_URL
// (declared in turbo.json globalEnv + vite/client types); it is optional, so the
// literal access is `string | undefined` under noUncheckedIndexedAccess. Fall back
// to the identity-service dev port so the app runs out of the box in local dev.

const raw = import.meta.env.VITE_API_BASE_URL;
export const API_BASE_URL =
  typeof raw === "string" && raw.length > 0 ? raw : "http://127.0.0.1:8001";
