import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Field } from "./field";

describe("Field", () => {
  it("links the label to the input", () => {
    render(<Field label="E-Mail" />);

    expect(screen.getByLabelText("E-Mail")).toBeInTheDocument();
  });

  it("keeps two fields with the same label independently addressable", () => {
    // useId statt eines festen id-Strings: sonst zeigt das zweite Label auf das
    // erste Feld und der Klick landet im falschen Eingabefeld.
    render(
      <>
        <Field label="Passwort" defaultValue="a" />
        <Field label="Passwort" defaultValue="b" />
      </>
    );

    const [first, second] = screen.getAllByLabelText("Passwort");
    expect(first).not.toBe(second);
    expect((first as HTMLInputElement).value).toBe("a");
    expect((second as HTMLInputElement).value).toBe("b");
  });

  it("announces hint and error together", () => {
    render(<Field label="E-Mail" hint="Geschäftlich oder privat" error="Adresse fehlt" />);

    const input = screen.getByLabelText("E-Mail");
    const describedBy = input.getAttribute("aria-describedby") ?? "";
    // Beide, nicht nur der Fehler — sonst verschluckt der Fehlerfall den Hinweis.
    expect(describedBy.split(" ")).toHaveLength(2);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("Adresse fehlt");
  });

  it("is not marked invalid without an error", () => {
    render(<Field label="E-Mail" />);

    expect(screen.getByLabelText("E-Mail")).not.toHaveAttribute("aria-invalid");
  });
});
