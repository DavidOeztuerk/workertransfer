import { render, renderHook, screen } from "@testing-library/react";
import { act } from "react";
import { describe, expect, it } from "vitest";

import { LiveRegion, useAnnounce } from "./live-region";

describe("LiveRegion", () => {
  it("announces politely", () => {
    render(<LiveRegion message="Freigabe erteilt" />);

    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("Freigabe erteilt");
  });

  // Die Region muss von Anfang an im Baum stehen. Wird sie erst mit der
  // Nachricht eingefügt, liest der Screenreader sie nicht vor — eine
  // Live-Region muss existieren, BEVOR sich ihr Inhalt ändert.
  it("renders an empty region while there is nothing to say", () => {
    render(<LiveRegion message="" />);

    expect(screen.getByRole("status")).toHaveTextContent("");
  });
});

describe("useAnnounce", () => {
  it("hands the text to the region", () => {
    const { result } = renderHook(() => useAnnounce());

    act(() => result.current.announce("Gespeichert"));

    expect(result.current.message).toContain("Gespeichert");
  });

  // Derselbe Text zweimal ändert den DOM-Knoten nicht, und dann liest der
  // Screenreader ihn beim zweiten Mal NICHT vor. „Gespeichert" nach dem
  // zweiten Speichern wäre stumm.
  it("changes the node even when the text repeats", () => {
    const { result } = renderHook(() => useAnnounce());

    act(() => result.current.announce("Gespeichert"));
    const first = result.current.message;
    act(() => result.current.announce("Gespeichert"));
    const second = result.current.message;

    expect(second).not.toBe(first);
    expect(second).toContain("Gespeichert");
  });

  it("says nothing before the first announcement", () => {
    const { result } = renderHook(() => useAnnounce());

    expect(result.current.message).toBe("");
  });
});
