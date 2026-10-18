import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { i18n } from "../../../core/i18n/i18n";

/**
 * Files from Google Drive and Dropbox (ADR-0054): no button without keys, no
 * script before the click, only the chosen file — and the Google token goes
 * back once the file is here.
 */

type Config = Record<string, string>;

async function draw(config: Config, onFile = vi.fn()) {
  (window as unknown as { __WT_CONFIG__: Config }).__WT_CONFIG__ = config;
  vi.resetModules();
  const { CloudFiles } = await import("./CloudFiles");
  render(
    <I18nextProvider i18n={i18n}>
      <CloudFiles extensions={["pdf", "png"]} onFile={onFile} />
    </I18nextProvider>,
  );
  return onFile;
}

/** Scripts "load" at once: the provider's global is put in place, then onload fires. */
function scriptsLoad(globals: () => void) {
  const append = document.head.appendChild.bind(document.head);
  vi.spyOn(document.head, "appendChild").mockImplementation(<T extends Node>(node: T): T => {
    const added = append(node);
    if (node instanceof HTMLScriptElement) {
      globals();
      queueMicrotask(() => node.onload?.(new Event("load")));
    }
    return added;
  });
}

const scripts = () => [...document.head.querySelectorAll("script")].map((script) => script.src);

beforeEach(() => {
  document.head.querySelectorAll("script").forEach((script) => script.remove());
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  delete (window as unknown as { Dropbox?: unknown }).Dropbox;
  delete (window as unknown as { google?: unknown }).google;
  delete (window as unknown as { gapi?: unknown }).gapi;
  (window as unknown as { __WT_CONFIG__: Config }).__WT_CONFIG__ = {};
});

describe("CloudFiles", () => {
  it("draws nothing without keys", async () => {
    await draw({});

    expect(screen.queryByRole("button", { name: /Dropbox|Google/ })).toBeNull();
  });

  it("loads Dropbox only on the click and hands over the chosen file", async () => {
    const onFile = await draw({ DROPBOX_APP_KEY: "app-key" });
    expect(scripts()).toEqual([]);

    scriptsLoad(() => {
      (window as unknown as { Dropbox: unknown }).Dropbox = {
        choose: (options: { success: (files: { link: string; name: string }[]) => void; linkType: string }) => {
          expect(options.linkType).toBe("direct");
          options.success([{ link: "https://dl.dropboxusercontent.com/x/zeugnis.pdf", name: "zeugnis.pdf" }]);
        },
      };
    });
    // Typed by its name, not by the answer: a provider may say octet-stream.
    vi.stubGlobal("fetch", vi.fn(async () => new Response("%PDF", { headers: { "content-type": "application/octet-stream" } })));

    await userEvent.click(screen.getByRole("button", { name: "Aus Dropbox" }));

    await vi.waitFor(() => expect(onFile).toHaveBeenCalled());
    const file = onFile.mock.calls[0]![0] as File;
    expect(file.name).toBe("zeugnis.pdf");
    expect(file.type).toBe("application/pdf");
    const script = document.getElementById("dropboxjs");
    expect(script?.getAttribute("data-app-key")).toBe("app-key");
  });

  it("asks Google for drive.file only, fetches the pick with the token, and gives the token back", async () => {
    const onFile = await draw({
      GOOGLE_PICKER_CLIENT_ID: "client",
      GOOGLE_PICKER_API_KEY: "api-key",
      GOOGLE_PICKER_APP_ID: "1234",
    });
    const revoke = vi.fn();
    let scope = "";

    scriptsLoad(() => {
      (window as unknown as { gapi: unknown }).gapi = { load: (_: string, done: { callback: () => void }) => done.callback() };
      (window as unknown as { google: unknown }).google = {
        accounts: {
          oauth2: {
            initTokenClient: (options: { scope: string; callback: (response: { access_token: string }) => void }) => {
              scope = options.scope;
              return { requestAccessToken: () => options.callback({ access_token: "token-1" }) };
            },
            revoke,
          },
        },
        picker: {
          ViewId: { DOCS: "docs" },
          Action: { PICKED: "picked", CANCEL: "cancel" },
          DocsView: class {
            setMimeTypes() {
              return this;
            }
          },
          PickerBuilder: class {
            private callback: (data: unknown) => void = () => {};
            setAppId() { return this; }
            setOAuthToken() { return this; }
            setDeveloperKey() { return this; }
            addView() { return this; }
            setCallback(callback: (data: unknown) => void) {
              this.callback = callback;
              return this;
            }
            build() {
              return { setVisible: () => this.callback({ action: "picked", docs: [{ id: "file-9", name: "meisterbrief.pdf" }] }) };
            }
          },
        },
      };
    });
    const fetched = vi.fn(async () => new Response("%PDF", { headers: { "content-type": "application/pdf" } }));
    vi.stubGlobal("fetch", fetched);

    await userEvent.click(screen.getByRole("button", { name: "Aus Google Drive" }));

    await vi.waitFor(() => expect(onFile).toHaveBeenCalled());
    expect(scope).toBe("https://www.googleapis.com/auth/drive.file");
    expect(fetched).toHaveBeenCalledWith(
      "https://www.googleapis.com/drive/v3/files/file-9?alt=media",
      { headers: { Authorization: "Bearer token-1" } },
    );
    expect((onFile.mock.calls[0]![0] as File).name).toBe("meisterbrief.pdf");
    expect(revoke).toHaveBeenCalledWith("token-1");
  });

  it("a closed window is no failure, a failed fetch is one", async () => {
    const onFile = await draw({ DROPBOX_APP_KEY: "app-key" });
    let cancel = true;

    scriptsLoad(() => {
      (window as unknown as { Dropbox: unknown }).Dropbox = {
        choose: (options: { success: (files: { link: string; name: string }[]) => void; cancel: () => void }) =>
          cancel ? options.cancel() : options.success([{ link: "https://dl.dropboxusercontent.com/x/a.pdf", name: "a.pdf" }]),
      };
    });
    vi.stubGlobal("fetch", vi.fn(async () => new Response("gone", { status: 404 })));

    await userEvent.click(screen.getByRole("button", { name: "Aus Dropbox" }));
    await vi.waitFor(() => expect(screen.getByRole("button", { name: "Aus Dropbox" })).toBeEnabled());
    expect(screen.queryByText(/ließ sich nicht holen/)).toBeNull();

    cancel = false;
    await userEvent.click(screen.getByRole("button", { name: "Aus Dropbox" }));

    expect(await screen.findByText(/ließ sich nicht holen/)).toBeInTheDocument();
    expect(onFile).not.toHaveBeenCalled();
  });
});
