---
name: wt-resume-documents
description: Use when working on a person's uploaded documents in WorkerTransfer's resume-service — files picked from Google Drive or Dropbox (ADR-0054), the shipped UI's Content-Security-Policy and nginx headers, reading documents for skill suggestions (ADR-0043, `PdfPig`, `TermFinder`, `POST /resumes/me/documents/read`), the term index `resume_document_terms`, or its erasure.
---

# resume-service: documents a person uploads

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liners stay there.

- `docs/adr/0054-dateien-aus-drive-und-dropbox.md` — files from Drive and Dropbox, only the chosen one; why OneDrive is not there. 18.10.2026.

### Files from Google Drive and Dropbox: only the chosen one, fetched in the browser

Built 18.10.2026 under [ADR-0054](../../../docs/adr/0054-dateien-aus-drive-und-dropbox.md). Google's Picker with `drive.file` and Dropbox's Chooser (an app key, no OAuth) put the chosen file into the CV page's existing upload form — **chosen is not stored**, only "Upload" files it, through `POST /resumes/me/documents`. No token reaches a service; Google's is revoked once the file is here. The providers' scripts load on the click, never with the page. The fetched file is typed by its ending, because a provider may answer `application/octet-stream`. **OneDrive is deliberately missing**: its picker v8 needs `Files.Read` on all of a person's files. Keys come from the runtime config (`GOOGLE_PICKER_*`, `DROPBOX_APP_KEY`); empty means no button. **The shipped UI carries a Content-Security-Policy** (20.10.2026, `docker/web-nginx.conf.template`): scripts from the bundle and the two pickers only, no inline script, Emotion's inline styles allowed, `connect-src` naming the gateway from `WT_API_ORIGIN` (the chart sets it from the same origin `config.js` points at), `frame-ancestors 'none'`. **nginx inherits `add_header` into a location only when the location sets none of its own** — so every location repeats the policy line, or `index.html` arrives unprotected; `WebHeaderTests` pins that. Measured against the built image: a foreign fetch, a foreign script and an inline script are refused. The Picker and the Chooser themselves are unmeasured until their keys exist — if one breaks, the console names the directive. The dev server (`:5173`) sends no policy; it is not what ships.

### Suggestions from a person's own documents — a library in-process, an index that falls with the erasure

Built 12.09.2026 under ADR-0043. It is PBI-7, and it closes the last place where
this platform was still built for developers: the suggestion area on the profile
page had three sources — GitHub topics, the technologies on a CV position, those
on a work sample — and the electrician who uploaded a certificate got nothing.

**It is a fourth source at an existing seam, not a second mechanism.** The
suggestion area and both of its acts have stood since ADR-0033, pinned by seven
rows in `ProfilePage.test.tsx`; a click fills the field, **Save** makes it a
naming. Words out of a document arrive in the same list, with no mark and no
precedence.

**The recogniser is a library in the process, and that is the decision the ADR
exists for.** `PdfPig` (MIT) pulls **zero** transitive packages — the same
measure that gave `Noelia.Http` the edge over `Noelia.Infrastructure`, and here
it counts twice: a reference names the employer, the dates, often the grade. A
recogniser on the network would mean that document leaving the platform so a
stranger can look for words in it, and the gain would be a suggestion in a form
field. There is therefore no address to put in the configuration — and nothing
the egress boundary could refuse without logging a line, which is exactly how
the cover-letter agent died on 10.09.2026. The reason sits at the registration
in `ResumeInfrastructure`, where somebody would swap the recogniser.

**No OCR on images, and the answer says so.** A photographed Meisterbrief is a
picture; so is a scan without a text layer. The answer therefore knows **three**
states and they must never become two: `read_at: null` (never read),
`has_text: false` (read, no text in it), and an empty `terms` with
`has_text: true` (read, no known word). Collapsing the last two is ADR-0022 §3
word for word — a view that does not say so lies by omission.

**Found is what the vocabulary knows, and nothing else.** `TermFinder` searches the
canonical names and spellings of `Vocabulary`: `schweissfachmann` in a reference
becomes `Schweißfachmann` — a statement about *language* (ADR-0023). The obvious
alternative is a heuristic ("capitalised words", "after `Kenntnisse:`"), and each
one is a guess about a person drawn from the *shape* of their document. What the
vocabulary does not know stays unfound; whoever misses a word extends the
vocabulary by pull request, never from people's documents. **Short abbreviations
count only in capitals** — "wig" is a hairpiece, "go" an everyday verb; below
four characters and pure letters, the occurrence must be upper case. `C#`, `C++`
and `k8s` carry a character that makes them unmistakable anyway.

**Triggered, and therefore never at upload.** "On trigger" is only half the
rule — a read running at upload would formally be triggered too, by the upload.
The recogniser is called from `POST /resumes/me/documents/read` and from nowhere
else; `GET /resumes/me/documents/terms` opens no file and writes no row, which is
why the profile page may ask it on load. **Measured rather than claimed:** the
suite wraps a counter around the *real* recogniser and reads **zero** calls after
an upload — an outcome check would have been weaker, since a read that runs and
discards its result has still read the reference. The button re-reads everything
every time, deliberately: the vocabulary grows by pull request, and a document
read before an extension would carry its find incomplete for ever.

**The index lives in resume-service and falls with the erasure.**
`resume_document_terms`, one row per document, next to the files it came from —
that condition is the whole permission for an index over personal text to exist
at all. It holds **names, never the wording**: a column with the text of a
reference would be a second copy of the document, this time in the database and
therefore in every backup (the same argument that puts the bytes in the
`DocumentStore`, ADR-0035). The word list is a field in the row rather than its own table,
because a table per word could be searched *by* the word.

**Both erasure paths are measured in the database, not in the answer.** After an
erasure the documents are gone too, so `/terms` answers empty anyway — a test
reading the answer would stay green while the table still held the words from a
deleted person's references.

**Not searchable, as a mechanism.** `ITermStore` has three methods — "what is
in *this* person's documents", save, delete. "Which people carry
‚Schweißfachmann' in their documents" does not exist, and `ConditionsTests` pins
the method set as a **set** at the interface *and* at its implementation, so a
fourth stands out instead of riding along. Five counter-probes fell, all of them
compiling: reading at upload; the erasure without the index; a `WerHatAsync` on
the store; an `uebernimm` that saves on the side; and the capitals rule removed
(then "she was wearing a wig" is a find).
