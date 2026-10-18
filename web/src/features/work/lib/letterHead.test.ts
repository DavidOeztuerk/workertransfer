import { describe, expect, it } from "vitest";

import { recipientFromCompany, senderFromSession } from "./letterHead";

describe("letterHead", () => {
  it("sets the sender left-aligned from account and postal address", () => {
    const head = senderFromSession(
      {
        userId: "1",
        email: "a@b.de",
        tenantId: null,
        language: "de",
        displayName: "Anna",
        givenName: "Anna",
        familyName: "Beispiel",
        occupationalField: null,
      },
      {
        line1: "Weg 2",
        line2: "",
        postalCode: "80331",
        city: "München",
        country: "DE",
        phone: "+4989123",
      },
    );

    expect(head.name).toBe("Anna Beispiel");
    expect(head.lines).toEqual(["Weg 2", "", "80331 München", "DE", "+4989123"]);
  });

  it("takes the company address as soon as the company has entered it", () => {
    const recipient = recipientFromCompany(
      {
        tenant_id: "t",
        slug: "muster",
        display_name: "Muster GmbH",
        about: "",
        website: "https://muster.example",
        locations: ["Hamburg"],
        benefits: [],
        line1: "Musterstraße 1",
        postal_code: "10115",
        city: "Berlin",
        country: "DE",
        phone: "",
        updated_at: "",
      },
      {
        id: "j",
        tenant_id: "t",
        title: "Stelle",
        description: "",
        location: "Hamburg",
        postal_code: "20095",
        remote: "none",
        employment: "full_time",
        skills: [],
        status: "published",
        published_at: null,
        updated_at: "",
        salary: null,
        mirrored: false,
        source_url: null,
      },
    );

    expect(recipient.name).toBe("Muster GmbH");
    expect(recipient.lines).toEqual(["Musterstraße 1", "10115 Berlin", "DE"]);
  });

  it("falls back to the job's place while there is no company profile", () => {
    const recipient = recipientFromCompany(null, {
      id: "j",
      tenant_id: "t",
      title: "Stelle",
      description: "",
      location: "Hamburg",
      postal_code: "20095",
      remote: "none",
      employment: "full_time",
      skills: [],
      status: "published",
      published_at: null,
      updated_at: "",
      salary: null,
      mirrored: false,
      source_url: null,
    });

    expect(recipient.name).toBe("");
    expect(recipient.lines).toEqual(["", "20095 Hamburg", ""]);
  });
});
