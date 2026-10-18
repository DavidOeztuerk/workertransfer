// Files from Google Drive and Dropbox (ADR-0054) — only the chosen one, fetched
// in the browser and handed to the existing upload. No token reaches a service
// of ours; the providers' scripts load on the click, never with the page.

import { DROPBOX_APP_KEY, GOOGLE_PICKER } from "../../../env";

export type CloudSource = "google" | "dropbox";

/** The sources this deployment has keys for — no key, no button. */
export function cloudSources(): CloudSource[] {
  const sources: CloudSource[] = [];
  if (GOOGLE_PICKER.clientId && GOOGLE_PICKER.apiKey && GOOGLE_PICKER.appId) sources.push("google");
  if (DROPBOX_APP_KEY) sources.push("dropbox");
  return sources;
}

/** The person closed the provider's window: nothing chosen, nothing wrong. */
export class CloudCancelled extends Error {}

const MIME: Record<string, string> = {
  pdf: "application/pdf",
  png: "image/png",
  jpg: "image/jpeg",
  jpeg: "image/jpeg",
};

const loading = new Map<string, Promise<void>>();

/** Loads a script once; a failed load may be tried again. */
export function loadScript(src: string, attributes: Record<string, string> = {}): Promise<void> {
  const known = loading.get(src);
  if (known) return known;

  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = src;
    script.async = true;
    for (const [name, value] of Object.entries(attributes)) script.setAttribute(name, value);
    script.onload = () => resolve();
    script.onerror = () => {
      loading.delete(src);
      script.remove();
      reject(new Error(`could not load ${src}`));
    };
    document.head.appendChild(script);
  });

  loading.set(src, promise);
  return promise;
}

/**
 * The file, typed by its name: a provider may answer `application/octet-stream`,
 * and the upload decides by type — what we accept has a known ending.
 */
async function asFile(response: Response, name: string): Promise<File> {
  if (!response.ok) throw new Error(`the provider answered ${response.status}`);
  const blob = await response.blob();
  const ending = name.split(".").pop()?.toLowerCase() ?? "";
  return new File([blob], name, { type: MIME[ending] ?? blob.type });
}

interface DropboxFile {
  link: string;
  name: string;
}

interface DropboxApi {
  choose(options: {
    success: (files: DropboxFile[]) => void;
    cancel: () => void;
    linkType: "direct";
    multiselect: false;
    folderselect: false;
    extensions: string[];
  }): void;
}

/** Dropbox's Chooser: a direct link, valid for four hours — fetched at once. */
export async function pickFromDropbox(extensions: string[]): Promise<File> {
  await loadScript("https://www.dropbox.com/static/api/2/dropins.js", {
    id: "dropboxjs",
    "data-app-key": DROPBOX_APP_KEY,
  });

  const dropbox = (window as unknown as { Dropbox?: DropboxApi }).Dropbox;
  if (!dropbox) throw new Error("the Dropbox chooser did not load");

  const chosen = await new Promise<DropboxFile>((resolve, reject) => {
    dropbox.choose({
      success: (files) => (files[0] ? resolve(files[0]) : reject(new CloudCancelled())),
      cancel: () => reject(new CloudCancelled()),
      linkType: "direct",
      multiselect: false,
      folderselect: false,
      extensions: extensions.map((extension) => `.${extension}`),
    });
  });

  return asFile(await fetch(chosen.link), chosen.name);
}

interface TokenResponse {
  access_token?: string;
  error?: string;
}

interface GoogleApi {
  accounts: {
    oauth2: {
      initTokenClient(options: {
        client_id: string;
        scope: string;
        callback: (response: TokenResponse) => void;
        error_callback?: (error: unknown) => void;
      }): { requestAccessToken(options?: { prompt?: string }): void };
      revoke(token: string, done?: () => void): void;
    };
  };
  picker: {
    ViewId: { DOCS: string };
    Action: { PICKED: string; CANCEL: string };
    DocsView: new (viewId: string) => { setMimeTypes(types: string): unknown };
    PickerBuilder: new () => PickerBuilder;
  };
}

interface PickerBuilder {
  setAppId(id: string): PickerBuilder;
  setOAuthToken(token: string): PickerBuilder;
  setDeveloperKey(key: string): PickerBuilder;
  addView(view: unknown): PickerBuilder;
  setCallback(callback: (data: { action: string; docs?: { id: string; name: string }[] }) => void): PickerBuilder;
  build(): { setVisible(visible: boolean): void };
}

/**
 * Google's Picker with `drive.file`: the app sees only what the person picks,
 * and the token is revoked once the file is here.
 */
export async function pickFromGoogle(extensions: string[]): Promise<File> {
  await Promise.all([
    loadScript("https://accounts.google.com/gsi/client"),
    loadScript("https://apis.google.com/js/api.js"),
  ]);

  const google = (window as unknown as { google?: GoogleApi }).google;
  const gapi = (window as unknown as { gapi?: { load(name: string, done: { callback: () => void }): void } }).gapi;
  if (!google || !gapi) throw new Error("the Google picker did not load");

  const token = await new Promise<string>((resolve, reject) => {
    google.accounts.oauth2
      .initTokenClient({
        client_id: GOOGLE_PICKER.clientId,
        scope: "https://www.googleapis.com/auth/drive.file",
        callback: (response) =>
          response.access_token ? resolve(response.access_token) : reject(new CloudCancelled()),
        error_callback: () => reject(new CloudCancelled()),
      })
      .requestAccessToken({ prompt: "" });
  });

  try {
    await new Promise<void>((resolve) => gapi.load("picker", { callback: resolve }));

    const types = [...new Set(extensions.map((extension) => MIME[extension]).filter(Boolean))].join(",");
    const view = new google.picker.DocsView(google.picker.ViewId.DOCS);
    view.setMimeTypes(types);

    const picked = await new Promise<{ id: string; name: string }>((resolve, reject) => {
      new google.picker.PickerBuilder()
        .setAppId(GOOGLE_PICKER.appId)
        .setOAuthToken(token)
        .setDeveloperKey(GOOGLE_PICKER.apiKey)
        .addView(view)
        .setCallback((data) => {
          if (data.action === google.picker.Action.PICKED && data.docs?.[0]) resolve(data.docs[0]);
          else if (data.action === google.picker.Action.CANCEL) reject(new CloudCancelled());
        })
        .build()
        .setVisible(true);
    });

    return await asFile(
      await fetch(`https://www.googleapis.com/drive/v3/files/${encodeURIComponent(picked.id)}?alt=media`, {
        headers: { Authorization: `Bearer ${token}` },
      }),
      picked.name,
    );
  } finally {
    // Kept for no longer than the file needed it.
    google.accounts.oauth2.revoke(token);
  }
}
