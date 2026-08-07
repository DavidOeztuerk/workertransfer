import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { MeResponse } from "../auth/client";
import { CompanyNewRoute } from "./company-new";

function principal(email: string | null): MeResponse {
  return { user_id: "u", email, tenant_id: null, roles: ["user"] };
}

describe("CompanyNewRoute", () => {
  it("is not offered to a private address", () => {
    // Sichtbarkeit ist Bequemlichkeit; die Ablehnung kommt vom Server (422).
    render(<CompanyNewRoute principal={principal("max@gmail.com")} />);

    expect(screen.getByText(/privaten Adresse/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Unternehmen anlegen" })).toBeNull();
  });

  it("shows the derived domain instead of asking for it", () => {
    render(<CompanyNewRoute principal={principal("anna@firma.de")} />);

    expect(screen.getByText("firma.de")).toBeInTheDocument();
    expect(screen.queryByLabelText("Domain")).toBeNull();
    expect(screen.getByRole("button", { name: "Unternehmen anlegen" })).toBeInTheDocument();
  });

  it("does not offer the form to an anonymous visitor", () => {
    render(<CompanyNewRoute principal={null} />);

    expect(screen.queryByRole("button", { name: "Unternehmen anlegen" })).toBeNull();
  });
});
