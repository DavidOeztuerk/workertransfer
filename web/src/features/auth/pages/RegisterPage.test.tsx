import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { RegisterPage } from "./RegisterPage";

type Answer = { status?: number; body?: unknown; throws?: boolean };

function answerInOrder(...sequence: Answer[]) {
  const asked: { path: string; body: string }[] = [];
  let number = 0;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      // The provider buttons ask on load (ADR-0053); that is not part of the
      // sequence these tests describe.
      if (new URL(url).pathname === "/auth/external") {
        return new Response(JSON.stringify({ providers: [] }), { status: 200 });
      }
      asked.push({
        path: new URL(url).pathname,
        body: String(init?.body ?? ""),
      });
      const reply = sequence[number] ??
        sequence[sequence.length - 1] ?? { status: 201 };
      number += 1;
      if (reply.throws === true) throw new TypeError("Failed to fetch");
      return new Response(JSON.stringify(reply.body ?? {}), {
        status: reply.status ?? 201,
      });
    }),
  );
  return asked;
}

async function fillAndSubmit(
  user: ReturnType<typeof userEvent.setup>,
  email: string,
) {
  await user.type(screen.getByLabelText(/E-Mail/i), email);
  await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
  await user.type(screen.getByLabelText(/Anzeigename/i), "Max");
  await user.click(screen.getByRole("button", { name: /Registrieren/i }));
}

beforeEach(() => {
  answerInOrder({ status: 201 });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("RegisterPage", () => {
  it("registers with a private address and shows the confirmation hint", async () => {
    // Private addresses are the normal case, not the exception (ADR-0017).
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "max@gmail.com");

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
  });

  it("asks for neither a tenant nor a company", () => {
    renderWithStore(<RegisterPage />, { route: "/register" });

    expect(screen.queryByLabelText(/Mandant/i)).toBeNull();
    expect(screen.queryByLabelText("Firma")).toBeNull();
    expect(screen.getByLabelText(/E-Mail/i)).toBeInTheDocument();
  });

  it("sends display_name in snake_case and never a tenant", async () => {
    const asked = answerInOrder({ status: 201 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");

    // The wire binds `display_name`. With `displayName` the input does not
    // arrive — measured, and the defect looked like an empty form.
    expect(asked[0]?.body).toContain('"display_name"');
    expect(asked[0]?.body).not.toContain("tenant_id");
  });

  it("sends given and family name in snake_case when they are there", async () => {
    const asked = answerInOrder({ status: 201 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Max");
    await user.type(screen.getByLabelText(/^Vorname$/i), "Maximilian");
    await user.type(screen.getByLabelText(/^Nachname$/i), "Muster");
    await user.click(screen.getByRole("button", { name: /Registrieren/i }));

    expect(asked[0]?.body).toContain('"given_name":"Maximilian"');
    expect(asked[0]?.body).toContain('"family_name":"Muster"');
  });

  it("says the same for a known address as for a new one", async () => {
    // The server answers 201 for a known address too — no enumeration channel.
    // The UI must not make anything else of it.
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "already-there@company.de");

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("RegisterPage — person or company", () => {
  it("registers a person by default, without asking for a company", () => {
    renderWithStore(<RegisterPage />, { route: "/register" });

    expect(screen.getByRole("radio", { name: "Für mich" })).toBeChecked();
    expect(screen.queryByLabelText(/Name des Unternehmens/i)).toBeNull();
  });

  it("asks for the company name only when a company registers", async () => {
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );

    expect(screen.getByLabelText(/Name des Unternehmens/i)).toBeInTheDocument();
  });

  it("asks a company for no civil name and no working world, and sends none typed before", async () => {
    // A company is registered by its contact; name and working world belong to
    // a person's own account.
    const asked = answerInOrder({ status: 201 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/^Vorname$/i), "Maximilian");
    await user.click(screen.getByRole("radio", { name: "Für ein Unternehmen" }));

    expect(screen.queryByLabelText(/^Vorname$/i)).toBeNull();
    expect(screen.queryByLabelText(/^Nachname$/i)).toBeNull();
    expect(screen.queryByLabelText(/Berufsfeld/i)).toBeNull();

    await user.type(screen.getByLabelText(/E-Mail/i), "max@example-gmbh.de");
    await user.type(screen.getByLabelText(/Name des Unternehmens/i), "Example GmbH");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Max");
    await user.click(screen.getByRole("button", { name: /Registrieren/i }));

    expect(asked[0]?.body).toContain('"company_name":"Example GmbH"');
    expect(asked[0]?.body).not.toContain("given_name");
    expect(asked[0]?.body).not.toContain("occupational_field");
  });

  it("preselects the company when the address says so", () => {
    // The hero buttons of the home page carry the intent along as ?as=company.
    renderWithStore(<RegisterPage />, { route: "/register?as=company" });

    expect(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    ).toBeChecked();
    expect(screen.getByLabelText(/Name des Unternehmens/i)).toBeInTheDocument();
  });

  it("says at once that a mass mail provider cannot become a company", async () => {
    // The server refuses anyway (422), but whoever learns it two steps later
    // has lost two steps.
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );
    await user.type(screen.getByLabelText(/E-Mail/i), "max@gmail.com");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Ein Unternehmen braucht eine eigene Domain.",
    );
    expect(
      screen.getByRole("button", { name: /Registrieren/i }),
    ).toBeDisabled();
  });

  it("does not judge an address that has no @ yet", async () => {
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register?as=company" });

    await user.type(screen.getByLabelText(/E-Mail/i), "ma");

    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("does not colour an unfinished address red while typing", async () => {
    // Judged only once a field was left.
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/E-Mail/i), "max@");

    expect(
      screen.queryByText(/sieht nicht nach einer E-Mail-Adresse aus/i),
    ).toBeNull();
  });

  it("says after leaving the field that the address is none", async () => {
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/E-Mail/i), "bus");
    await user.tab();

    expect(
      screen.getByText(/sieht nicht nach einer E-Mail-Adresse aus/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Registrieren/i }),
    ).toBeDisabled();
  });

  it("checks freemail only when claiming a domain, not for a person", async () => {
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    // The whole form: the button is also locked while required fields are
    // empty, and this test speaks about freemail.
    await user.type(screen.getByLabelText(/E-Mail/i), "max@gmail.com");
    await user.type(screen.getByLabelText(/Passwort/i), "a-long-password");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Max");

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("button", { name: /Registrieren/i })).toBeEnabled();
  });

  it("sends the company name, and never a tenant", async () => {
    const asked = answerInOrder({ status: 201 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );
    await user.type(screen.getByLabelText(/E-Mail/i), "boss@company.de");
    await user.type(
      screen.getByLabelText(/Name des Unternehmens/i),
      "Example GmbH",
    );
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Boss");
    await user.click(screen.getByRole("button", { name: /Registrieren/i }));

    expect(asked[0]?.body).toContain('"company_name":"Example GmbH"');
    expect(asked[0]?.body).not.toContain("tenant_id");
  });

  it("sends no company_name at all as a person", async () => {
    const asked = answerInOrder({ status: 201 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "person@example.com");

    // No `"company_name":null` — a null would be a statement nobody made.
    expect(asked[0]?.body).not.toContain("company_name");
  });
});

/*
 * A GERMAN PERSON READS GERMAN when identity-service refuses.
 *
 * Until 26.09.2026 this page drew the problem document's `detail` raw, and
 * since the backend answers in English a German person read "Password rejected:
 * exceeds 72 bytes". The refusal is chosen by status and sentence now
 * (`api/registration.ts`); none of the cases depends on whether the address is
 * known — the server answers 201 for a known one, too.
 */
describe("RegisterPage — a refusal in the person's language", () => {
  it("names the gateway's brake in German", async () => {
    answerInOrder({ status: 429, body: { title: "Request failed", detail: "Too many requests" } });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Zu viele Versuche in kurzer Zeit.",
    );
    expect(document.body.textContent).not.toMatch(/Too many requests/);
  });

  it("names a password the server refuses as too long", async () => {
    answerInOrder({
      status: 400,
      body: { title: "Request failed", detail: "Password rejected: exceeds 72 bytes" },
    });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");

    expect(await screen.findByRole("alert")).toHaveTextContent("Das Passwort ist zu lang.");
    expect(document.body.textContent).not.toMatch(/Password rejected/);
  });
});

describe("RegisterPage — requesting the mail again", () => {
  it("says so when the mail could not even be requested", async () => {
    // On a network error the button visibly did NOTHING.
    answerInOrder({ status: 201 }, { throws: true });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die E-Mail konnte gerade nicht angefordert werden.",
    );
    expect(screen.queryByText(/erneut unterwegs/)).toBeNull();
  });

  /**
   * The second failure that used to pass as a success: the brake
   * (`/auth/resend-verification`, 3/min) answers 429. The old code caught only
   * the transport error — the promise "on its way" stood there while nothing
   * was on its way.
   */
  it("reports a non-2xx as a failure too, instead of claiming a dispatch", async () => {
    answerInOrder(
      { status: 201 },
      { status: 429, body: { detail: "Too many requests" } },
    );
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die E-Mail konnte gerade nicht angefordert werden.",
    );
    expect(screen.queryByText(/erneut unterwegs/)).toBeNull();
  });

  it("confirms the dispatch when it went out", async () => {
    answerInOrder({ status: 201 }, { status: 202 });
    const user = userEvent.setup();
    renderWithStore(<RegisterPage />, { route: "/register" });

    await fillAndSubmit(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    // role="status" and not "alert": a confirmation does not interrupt.
    expect(await screen.findByRole("status")).toHaveTextContent(
      "erneut unterwegs",
    );
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("tells a founder what happens next instead of the person's promise", async () => {
    answerInOrder();
    renderWithStore(<RegisterPage />, { route: "/register?as=company" });

    expect(await screen.findByText(/sobald du deine Arbeitsadresse bestätigst/)).toBeInTheDocument();
    expect(screen.queryByText(/Sichtbar wirst du erst/)).not.toBeInTheDocument();
  });
});
