import { describe, expect, it } from "vitest";

import {
  PASSWORD_MAX_BYTES,
  PASSWORD_MIN_LENGTH,
  emailError,
  looksLikeAddress,
  passwordBytes,
  passwordError,
} from "./formValidation";

/**
 * The form carries `noValidate`, so `type="email"` blocks nothing. Without a
 * check of our own, strings without an `@` get through.
 */
describe("the validation of the registration form", () => {
  it("rejects a string without @", () => {
    expect(looksLikeAddress("bus")).toBe(false);
    expect(emailError("bus")).toBe("register.emailInvalid");
  });

  it.each([
    ["", "empty"],
    ["@example.org", "nothing before"],
    ["anna@", "nothing after"],
    ["anna@localhost", "no dot in the domain"],
    ["anna@example.", "dot at the edge"],
    ["anna example.org", "space"],
    ["a@b@c.de", "two at signs"],
  ])("rejects %s (%s)", (address) => {
    expect(looksLikeAddress(address)).toBe(false);
  });

  it.each([
    "anna@example.org",
    "anna.example@company.co.uk",
    "anna+application@example.org",
    "a@b.de",
    "jörg@example.org",
  ])("lets %s through", (address) => {
    // Generous: the refusal is the server's.
    expect(looksLikeAddress(address)).toBe(true);
  });

  it("shows no error yet on an empty field", () => {
    expect(emailError("")).toBeNull();
    expect(emailError("   ")).toBeNull();
    expect(passwordError("")).toBeNull();
  });

  it("measures the password in bytes, because bcrypt counts bytes", () => {
    // "ä" is two bytes.
    expect(passwordBytes("ä")).toBe(2);

    const justFits = "ä".repeat(PASSWORD_MAX_BYTES / 2);
    expect(passwordError(justFits)).toBeNull();
    expect(passwordError(`${justFits}a`)).toBe("register.passwordTooLong");
  });

  it("names the minimum length before anyone submits", () => {
    expect(passwordError("a".repeat(PASSWORD_MIN_LENGTH - 1))).toBe(
      "register.passwordTooShort",
    );
    expect(passwordError("a".repeat(PASSWORD_MIN_LENGTH))).toBeNull();
  });
});
