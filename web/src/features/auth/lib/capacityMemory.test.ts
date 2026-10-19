import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  capacityRestoreReset,
  companyToRestore,
  rememberCapacity,
} from "./capacityMemory";

const ANNA = "11111111-1111-1111-1111-111111111111";
const MUSTER = { id: "22222222-2222-4222-8222-222222222222", name: "Muster GmbH", role: "admin" };
const OTHER = { id: "33333333-3333-4333-8333-333333333333", name: "Andere AG", role: "member" };

beforeEach(() => {
  localStorage.clear();
  capacityRestoreReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("capacityMemory", () => {
  it("lets whoever registered their one company start acting for it", () => {
    expect(companyToRestore(ANNA, [MUSTER])).toBe(MUSTER.id);
  });

  it("starts a plain member as themselves", () => {
    expect(companyToRestore(ANNA, [{ ...MUSTER, role: "member" }])).toBeNull();
  });

  it("starts somebody with two companies as themselves until they chose", () => {
    expect(companyToRestore(ANNA, [MUSTER, OTHER])).toBeNull();
  });

  it("keeps a chosen self, even for a founder", () => {
    rememberCapacity(ANNA, null);
    capacityRestoreReset();

    expect(companyToRestore(ANNA, [MUSTER])).toBeNull();
  });

  it("brings back the chosen company", () => {
    rememberCapacity(ANNA, OTHER.id);
    capacityRestoreReset();

    expect(companyToRestore(ANNA, [MUSTER, OTHER])).toBe(OTHER.id);
  });

  it("forgets a company one no longer belongs to", () => {
    rememberCapacity(ANNA, OTHER.id);
    capacityRestoreReset();

    expect(companyToRestore(ANNA, [MUSTER, { ...MUSTER, id: "44444444-4444-4444-8444-444444444444" }])).toBeNull();
    expect(localStorage.getItem(`wt.actingAs.${ANNA}`)).toBeNull();
  });

  it("restores once per sign-in, so going back to oneself holds without storage", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("private window");
    });

    expect(companyToRestore(ANNA, [MUSTER])).toBe(MUSTER.id);
    rememberCapacity(ANNA, null);

    expect(companyToRestore(ANNA, [MUSTER])).toBeNull();
  });
});
