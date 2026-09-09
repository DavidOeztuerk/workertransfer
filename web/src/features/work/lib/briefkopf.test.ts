import { describe, expect, it } from "vitest";

import { empfaengerAusFirma, kopfAusSitzung } from "./briefkopf";

describe("briefkopf", () => {
  it("setzt Absender linksbündig aus Konto und Anschrift", () => {
    const kopf = kopfAusSitzung(
      {
        userId: "1",
        email: "a@b.de",
        tenantId: null,
        language: "de",
        displayName: "Anna",
        givenName: "Anna",
        familyName: "Beispiel",
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

    expect(kopf.name).toBe("Anna Beispiel");
    expect(kopf.zeilen).toEqual(["Weg 2", "", "80331 München", "DE", "+4989123"]);
  });

  it("nimmt Firmenanschrift, sobald das Unternehmen sie eingetragen hat", () => {
    const an = empfaengerAusFirma(
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
      },
    );

    expect(an.name).toBe("Muster GmbH");
    expect(an.zeilen).toEqual(["Musterstraße 1", "10115 Berlin", "DE"]);
  });

  it("fällt auf den Stellenort zurück, solange kein Firmenprofil da ist", () => {
    const an = empfaengerAusFirma(null, {
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
    });

    expect(an.name).toBe("");
    expect(an.zeilen).toEqual(["", "20095 Hamburg", ""]);
  });
});
