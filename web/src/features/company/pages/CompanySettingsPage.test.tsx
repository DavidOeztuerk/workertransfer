import userEvent from "@testing-library/user-event";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { CompanySettingsPage } from "./CompanySettingsPage";

/**
 * The company's own AI key: an admin chooses and stores it, a member reads who
 * may, and the key itself never comes back to the page.
 */

const TENANT = "22222222-2222-4222-8222-222222222222";

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@firma.de",
    tenantId: TENANT,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });

const STORED = {
  ai_provider: "anthropic",
  ai_base_url: "https://api.anthropic.com/v1/messages",
  ai_model: "claude-opus-5-5",
  ai_key_present: true,
  ai_key_tail: "7f3a",
};

function stubFetch(answer: (url: string, init?: RequestInit) => Response) {
  const calls: { url: string; init?: RequestInit }[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      calls.push({ url, init });
      return answer(url, init);
    }),
  );
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the company settings", () => {
  it("shows the stored provider and only the key's last four characters", async () => {
    stubFetch((url) => (url.endsWith(`/companies/${TENANT}/ai`) ? json(200, STORED) : json(200, null)));
    renderWithStore(<CompanySettingsPage />, { auth: FOR_COMPANY });

    expect(await screen.findByText("Hinterlegt, endet auf 7f3a")).toBeInTheDocument();
    expect(screen.getByLabelText(/Modell/)).toHaveValue("claude-opus-5-5");
  });

  it("sends the key to its own route, one way, and empties the field", async () => {
    const calls = stubFetch((url) => (url.endsWith("/ai/key") ? new Response(null, { status: 204 }) : json(200, STORED)));
    renderWithStore(<CompanySettingsPage />, { auth: FOR_COMPANY });

    const field = await screen.findByLabelText(/Schlüssel/);
    fireEvent.change(field, { target: { value: "sk-ant-new" } });
    fireEvent.click(screen.getByRole("button", { name: "Schlüssel speichern" }));

    await waitFor(() => expect(calls.some((call) => call.url.endsWith(`/companies/${TENANT}/ai/key`))).toBe(true));
    const sent = calls.find((call) => call.url.endsWith("/ai/key"))!;
    expect(sent.init?.method).toBe("PUT");
    expect(JSON.parse(String(sent.init?.body))).toEqual({ key: "sk-ant-new" });
    await waitFor(() => expect(field).toHaveValue(""));
  });

  it("tells a member who may, instead of offering a form", async () => {
    stubFetch(() => json(403, { title: "Forbidden", detail: "not permitted", status: 403 }));
    renderWithStore(<CompanySettingsPage />, { auth: FOR_COMPANY });

    expect(await screen.findByText(/stellt nur ein Admin ein/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Anbieter/)).not.toBeInTheDocument();
  });

  it("saves an own server only with its address and model, and names the rule when refused", async () => {
    const NONE = { ...STORED, ai_provider: "none", ai_base_url: "", ai_model: "", ai_key_present: false, ai_key_tail: "" };
    const calls = stubFetch((url, init) =>
      init?.method === "PUT"
        ? json(422, { title: "Request failed", detail: "invalid: ai_base_url", status: 422 })
        : url.endsWith(`/companies/${TENANT}/ai`) ? json(200, NONE) : json(200, null),
    );
    renderWithStore(<CompanySettingsPage />, { auth: FOR_COMPANY });

    await userEvent.click(await screen.findByLabelText(/Anbieter/));
    await userEvent.click(await screen.findByRole("option", { name: "Eigener Server (OpenAI-kompatibel)" }));

    // Choosing it alone saves nothing — it would only be refused.
    expect(calls.some((call) => call.init?.method === "PUT")).toBe(false);
    const save = screen.getByRole("button", { name: "Server speichern" });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/Adresse des Servers/), {
      target: { value: "https://ki.andere-firma.de/v1/chat/completions" },
    });
    fireEvent.change(screen.getByLabelText(/^Modell/), { target: { value: "qwen2.5:14b" } });
    await userEvent.click(save);

    await waitFor(() => expect(calls.some((call) => call.init?.method === "PUT")).toBe(true));
    const sent = JSON.parse(String(calls.find((call) => call.init?.method === "PUT")!.init?.body));
    expect(sent).toEqual({
      ai_provider: "own",
      ai_model: "qwen2.5:14b",
      ai_base_url: "https://ki.andere-firma.de/v1/chat/completions",
    });
    expect(await screen.findByText(/unter eurer bestätigten Domain\. Ein Modell/)).toBeInTheDocument();
  });

  it("asks whoever acts as a person to switch to a company", async () => {
    stubFetch(() => json(200, null));
    renderWithStore(<CompanySettingsPage />, {
      auth: { ...FOR_COMPANY, session: { ...FOR_COMPANY.session, tenantId: null } },
    });

    expect(await screen.findByText(/Wechsle im Kontomenü auf ein Unternehmen/)).toBeInTheDocument();
  });
});
