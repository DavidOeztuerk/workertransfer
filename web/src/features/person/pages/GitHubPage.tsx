import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { ErrorBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import { type Verbindung, holeNeu, ladeMeine, nenneKonto, pruefeNachweis, trenne } from "../api/github";

/**
 * <c>/github</c> — die eigene, nachgewiesene Verbindung.
 *
 * <strong>Belege, keine Noten.</strong> Was hier steht, sind öffentliche
 * Repositories mit Link. Diese Plattform rechnet daraus keine Punktzahl und
 * keine Rangfolge — wer wissen will, ob der Code gut ist, sieht ihn sich an.
 * Das ist nicht Zurückhaltung, sondern die Linie: Anforderung rein, Belege
 * raus; nie Mensch rein, Zahl raus.
 *
 * <strong>Und keine stille Unvollständigkeit.</strong> Keine Repositories sind
 * kein Mangel, sondern eine Auskunft — wer nichts auf GitHub hat, ist nicht
 * schlechter, sondern woanders. Die Seite sagt das ausdrücklich.
 *
 * <strong>Geholt wird nur auf Auslösung.</strong> Kein Abgleich im Hintergrund.
 */
export function GitHubPage() {
  const status = useAppSelector((state) => state.auth.status);
  const sitzung = useAppSelector((state) => state.auth.session);

  const [login, setLogin] = useState("");
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const verbindung = useAsync(
    (signal) => ladeMeine(signal),
    [sitzung?.userId],
    sitzung !== null
  );

  if (status === "anonymous") {
    return <AnmeldungNoetig titel="GitHub verbinden" zweck="dein GitHub-Konto zu verbinden" />;
  }

  async function fuehreAus(was: () => Promise<{ ok: boolean; error?: { detail: string } }>) {
    setLaeuft(true);
    const ergebnis = await was();
    setLaeuft(false);

    if (!ergebnis.ok && ergebnis.error) setFehler(ergebnis.error.detail);
    else setFehler(null);

    verbindung.erneut();
  }

  const geladen = !verbindung.laedt && verbindung.wert?.ok === true;
  const stand: Verbindung | null = verbindung.wert?.ok ? verbindung.wert.wert : null;

  return (
    <PageShell
      title="GitHub verbinden"
      narrow
      lead={
        "Was hier erscheint, sind Belege, keine Noten: deine öffentlichen Repositories mit Link. "
        + "Diese Plattform rechnet daraus keine Punktzahl und keine Rangfolge — wer wissen will, "
        + "ob dein Code gut ist, sieht ihn sich an."
      }
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        Geholt wird nur, wenn du es auslöst. Es läuft kein Abgleich im Hintergrund: eine
        Plattform, die dir dauerhaft hinterhersieht, tut etwas anderes als eine, die einmal auf
        deine Bitte hinsieht.
      </Typography>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      {verbindung.laedt ? (
        <Card>
          <CardContent>
            <LoadingBlock label="Verbindung wird geladen…" />
          </CardContent>
        </Card>
      ) : null}

      {verbindung.wert && !verbindung.wert.ok ? <ErrorBlock error={verbindung.wert.error} /> : null}

      {/* `geladen` gehört dazu: solange geladen wird, ist der Stand unbekannt,
          und vorher stand „Wird geladen…" UND das Formular gleichzeitig da. Wer
          schnell tippt, nannte ein Konto, bevor die Seite wusste, ob schon eines
          verbunden ist. */}
      {geladen && stand === null ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 2 }}>
              Konto nennen
            </Typography>
            <Box
              component="form"
              onSubmit={(ereignis) => {
                ereignis.preventDefault();
                void fuehreAus(() => nenneKonto(login));
              }}
              sx={{ display: "flex", flexDirection: "column", gap: 2, alignItems: "flex-start" }}
            >
              <TextField
                label="GitHub-Benutzername"
                helperText="Nur der Name, ohne Adresse."
                placeholder="anna"
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                slotProps={{ htmlInput: { maxLength: 39 } }}
                required
              />
              <Button type="submit" variant="contained" disabled={laeuft}>
                Weiter
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}

      {stand !== null && !stand.verified ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1.5 }}>
              Nachweis
            </Typography>
            <Typography sx={{ mb: 1.5 }}>
              Lege einen <strong>öffentlichen</strong> Gist an, dessen Beschreibung genau so
              lautet:
            </Typography>
            <Box
              component="pre"
              className="github__challenge"
              sx={{
                p: 2,
                borderRadius: 1,
                bgcolor: "action.hover",
                fontFamily: "monospace",
                fontSize: "0.875rem",
                overflowX: "auto",
              }}
            >
              {stand.challenge_description}
            </Box>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              Der Inhalt ist egal. Danach darf der Gist wieder weg — er beweist nur, dass du über
              das Konto <strong>{stand.login}</strong> verfügst.
            </Typography>
            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button
                variant="contained"
                onClick={() => void fuehreAus(() => pruefeNachweis())}
                disabled={laeuft}
              >
                {laeuft ? "Wird geprüft…" : "Nachweis prüfen"}
              </Button>
              <Button
                variant="text"
                onClick={() => void fuehreAus(async () => ({ ok: await trenne() }))}
                disabled={laeuft}
              >
                Anderes Konto
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}

      {stand !== null && stand.verified ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              {stand.login}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              {stand.fetched_at !== null
                ? `Stand: ${new Date(stand.fetched_at).toLocaleString("de-DE")}`
                : "Noch nichts geholt."}{" "}
              · Sichtbar wird das erst, wenn du es unter{" "}
              <Link component={RouterLink} to="/consents">
                Meine Freigaben
              </Link>{" "}
              freigibst.
            </Typography>

            {stand.repositories.length === 0 ? (
              <Typography sx={{ mb: 2 }}>
                Keine öffentlichen Repositories gefunden. Das ist kein Mangel — nur eine Auskunft.
              </Typography>
            ) : (
              <Box component="ul" sx={{ pl: 2.5, mb: 2 }}>
                {stand.repositories.map((repo) => (
                  <Box component="li" key={repo.name} sx={{ mb: 1 }}>
                    <Link href={repo.url} target="_blank" rel="noreferrer noopener">
                      {repo.name}
                    </Link>
                    <Typography variant="body2" color="text.secondary">
                      {repo.language ?? "ohne Sprachangabe"} · {repo.stars} ★
                      {repo.description !== "" ? ` · ${repo.description}` : ""}
                    </Typography>
                  </Box>
                ))}
              </Box>
            )}

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button
                variant="outlined"
                onClick={() => void fuehreAus(() => holeNeu())}
                disabled={laeuft}
              >
                {laeuft ? "Wird geholt…" : "Aktualisieren"}
              </Button>
              <Button
                variant="text"
                onClick={() => void fuehreAus(async () => ({ ok: await trenne() }))}
                disabled={laeuft}
              >
                Verbindung trennen
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}
    </PageShell>
  );
}
