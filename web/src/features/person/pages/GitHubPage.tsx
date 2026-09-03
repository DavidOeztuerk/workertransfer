import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import {
  type Verbindung,
  holeNeu,
  ladeMeine,
  nenneKonto,
  pruefeNachweis,
  trenne,
} from "../api/github";

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
  const { t, i18n } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const sitzung = useAppSelector((state) => state.auth.session);

  const [login, setLogin] = useState("");
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const verbindung = useAsync(
    (signal) => ladeMeine(signal),
    [sitzung?.userId],
    sitzung !== null,
  );

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig titel={t("github.titel")} satz="github.anmelden" />
    );
  }

  async function fuehreAus(
    was: () => Promise<{ ok: boolean; error?: { detail: string } }>,
  ) {
    setLaeuft(true);
    const ergebnis = await was();
    setLaeuft(false);

    if (!ergebnis.ok && ergebnis.error) setFehler(ergebnis.error.detail);
    else setFehler(null);

    verbindung.erneut();
  }

  const geladen = !verbindung.laedt && verbindung.wert?.ok === true;
  const stand: Verbindung | null = verbindung.wert?.ok
    ? verbindung.wert.wert
    : null;

  return (
    <PageShell
      title={t("github.titel")}
      narrow
      lead={t("github.lead")}
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        {t("github.nurAufBitte")}
      </Typography>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      {verbindung.laedt ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("github.laden")} />
          </CardContent>
        </Card>
      ) : null}

      {verbindung.wert && !verbindung.wert.ok ? (
        <ErrorBlock error={verbindung.wert.error} />
      ) : null}

      {/* `geladen` gehört dazu: solange geladen wird, ist der Stand unbekannt,
          und vorher stand „Wird geladen…" UND das Formular gleichzeitig da. Wer
          schnell tippt, nannte ein Konto, bevor die Seite wusste, ob schon eines
          verbunden ist. */}
      {geladen && stand === null ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 2 }}>
              {t("github.kontoNennen")}
            </Typography>
            <Box
              component="form"
              onSubmit={(ereignis) => {
                ereignis.preventDefault();
                void fuehreAus(() => nenneKonto(login));
              }}
              sx={{
                display: "flex",
                flexDirection: "column",
                gap: 2,
                alignItems: "flex-start",
              }}
            >
              <TextField
                label={t("github.benutzername")}
                helperText={t("github.benutzernameHinweis")}
                placeholder="anna"
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                slotProps={{ htmlInput: { maxLength: 39 } }}
                required
              />
              <Button type="submit" variant="contained" disabled={laeuft}>
                {t("github.weiter")}
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}

      {stand !== null && !stand.verified ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1.5 }}>
              {t("github.nachweis")}
            </Typography>
            <Typography sx={{ mb: 1.5 }}>
              <Trans
                i18nKey="github.nachweisAnleitung"
                components={{ 1: <strong /> }}
              />
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
              <Trans
                i18nKey="github.nachweisErklaerung"
                values={{ login: stand.login }}
                components={{ 1: <strong /> }}
              />
            </Typography>
            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button
                variant="contained"
                onClick={() => void fuehreAus(() => pruefeNachweis())}
                disabled={laeuft}
              >
                {laeuft ? t("github.nachweisLaeuft") : t("github.nachweisPruefen")}
              </Button>
              <Button
                variant="text"
                onClick={() =>
                  void fuehreAus(async () => ({ ok: await trenne() }))
                }
                disabled={laeuft}
              >
                {t("github.anderesKonto")}
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
                ? t("github.standVom", {
                    zeitpunkt: new Date(stand.fetched_at).toLocaleString(
                      i18n.language,
                    ),
                  })
                : t("github.nochNichtsGeholt")}{" "}
              <Trans
                i18nKey="github.sichtbarkeit"
                components={{
                  1: <Link component={RouterLink} to="/consents" />,
                }}
              />
            </Typography>

            {stand.repositories.length === 0 ? (
              <Typography sx={{ mb: 2 }}>
                {t("github.keineRepos")}
              </Typography>
            ) : (
              <Box component="ul" sx={{ pl: 2.5, mb: 2 }}>
                {stand.repositories.map((repo) => (
                  <Box component="li" key={repo.name} sx={{ mb: 1 }}>
                    <Link
                      href={repo.url}
                      target="_blank"
                      rel="noreferrer noopener"
                    >
                      {repo.name}
                    </Link>
                    <Typography variant="body2" color="text.secondary">
                      {repo.language ?? t("github.ohneSprache")} · {repo.stars}{" "}
                      ★
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
                {laeuft
                  ? t("github.aktualisierenLaeuft")
                  : t("github.aktualisieren")}
              </Button>
              <Button
                variant="text"
                onClick={() =>
                  void fuehreAus(async () => ({ ok: await trenne() }))
                }
                disabled={laeuft}
              >
                {t("github.trennen")}
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}
    </PageShell>
  );
}
