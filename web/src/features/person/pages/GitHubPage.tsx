import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Divider from "@mui/material/Divider";
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
  anmeldungAbschliessen,
  anmeldungBeginnen,
  anmeldungMoeglich,
  holeNeu,
  ladeMeine,
  nenneKonto,
  pruefeNachweis,
  trenne,
} from "../api/github";
import { getMyProfile, saveMyProfile } from "../api/profile";
import { ausRepositories, vorschlaegeAus } from "../lib/vorschlaege";

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
  const session = useAppSelector((state) => state.auth.session);

  const [login, setLogin] = useState("");
  /** Ist der Weg von Hand aufgeklappt? Zu, solange die Anmeldung genügt. */
  const [vonHand, setVonHand] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);
  const [uebernehmen, setUebernehmen] = useState(false);

  const connection = useAsync(
    (signal) => ladeMeine(signal),
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig titel={t("github.titel")} satz="github.anmelden" />
    );
  }

  async function run(
    was: () => Promise<{ ok: boolean; error?: { detail: string } }>,
  ) {
    setLaeuft(true);
    const result = await was();
    setLaeuft(false);

    if (!result.ok && result.error) setFehler(result.error.detail);
    else setFehler(null);

    connection.again();
  }

  const loaded = !connection.laedt && connection.value?.ok === true;
  const current: Verbindung | null = connection.value?.ok
    ? connection.value.value
    : null;

  /*
   * OB es die Anmeldung gibt, wird beim Betrachten geholt — WOHIN sie führt,
   * erst auf Klick.
   *
   * Der Unterschied ist nicht kosmetisch: `anmeldungBeginnen` legt eine
   * Verbindung an, wenn es noch keine gibt. Vorgeholt entstünde eine Zeile für
   * jeden, der die Seite einmal geöffnet und dann weitergeklickt hat.
   */
  const moeglich = useAsync(
    (signal) => anmeldungMoeglich(signal),
    [session?.userId],
    session !== null,
  );
  const mitAnmeldung = moeglich.value === true;

  /*
   * EIN GENANNTES KONTO IST KEIN GRUND, DEN GIST AUFZUZWINGEN.
   *
   * Hier stand die umgekehrte Regel — „wer ein Konto genannt hat, steckt im
   * Weg von Hand" —, damit ein Neuladen die Zeichenfolge nicht versteckt. Am
   * echten Stand gemessen war die Reparatur schlimmer als der Schaden: einen
   * Namen nennt auch, wer sich danach doch anmelden will, und der bekam den
   * Gist dann dauerhaft ins Bild. Die Zeichenfolge ist nach einem Neuladen
   * einen Klick entfernt und ändert sich dabei nicht.
   *
   * Was NICHT zuklappen darf, ist der Ausweg: wer einen falschen Namen stehen
   * hat, scheitert an der Anmeldung (der Vergleich lehnt ab) und braucht
   * „Anderes Konto" — der Knopf steht deshalb oben und nicht im Abschnitt.
   */
  const genanntesKonto = current?.login != null && !current.verified
    ? current.login
    : null;

  /** Anmeldung beginnen und hingehen. */
  async function meldeAn() {
    setLaeuft(true);
    const ziel = await anmeldungBeginnen();
    setLaeuft(false);

    if (ziel === null) {
      setFehler(t("github.anmeldungFehlgeschlagen"));
      return;
    }

    window.location.assign(ziel);
  }

  /*
   * DIE RÜCKKEHR VON GITHUB.
   *
   * Der Einmalcode steht in der Adresszeile. Er wird eingelöst und die Adresse
   * danach BEREINIGT — ein Code, der im Verlauf des Browsers stehen bleibt,
   * ist ein Geheimnis an einem Ort, an dem niemand eines vermutet. Verbraucht
   * ist er ohnehin, aber das weiss nur der Server.
   */
  const [eingeloest, setzeEingeloest] = useState(false);

  useEffect(() => {
    if (eingeloest) return;

    const adresse = new URLSearchParams(window.location.search);
    const code = adresse.get("code");
    const zustand = adresse.get("state");

    if (code === null || zustand === null) return;

    setzeEingeloest(true);
    window.history.replaceState({}, "", window.location.pathname);

    void anmeldungAbschliessen(code, zustand).then((ergebnis) => {
      if (!ergebnis.ok) {
        setFehler(t("github.anmeldungFehlgeschlagen"));
        return;
      }

      setFehler(null);
      connection.again();
    });
  }, [eingeloest, connection, t]);

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

      {connection.laedt ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("github.laden")} />
          </CardContent>
        </Card>
      ) : null}

      {connection.value && !connection.value.ok ? (
        <ErrorBlock error={connection.value.error} />
      ) : null}

      {/*
        DER SCHNELLE WEG IST DER ERSTE, UND ER VERLANGT KEINEN NAMEN.

        Vorher stand hier ein Formular: erst das Konto tippen, dann beweisen.
        Das war die Anforderung des GISTS — der muss wissen, in wessen Gists er
        sucht. Die Anmeldung braucht sie nicht: GitHub meldet allein das Konto,
        das wirklich zugestimmt hat, also gibt es nichts zu vergleichen. Der
        Namensschritt hat davor niemanden geschützt, nur ausgesperrt, wer sich
        vertippt hat oder noch einen alten Namen stehen hatte.

        Der Weg von Hand bleibt vollständig — er ist der einzige, wenn dieser
        Server keine Anmeldung eingerichtet hat. Dann steht er auch offen da.
      */}
      {loaded && (current === null || !current.verified) ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1.5 }}>
              {t("github.nachweis")}
            </Typography>

            {mitAnmeldung ? (
              <Box sx={{ mb: vonHand ? 3 : 0 }}>
                <Button
                  variant="contained"
                  onClick={() => void meldeAn()}
                  disabled={running}
                >
                  {t("github.mitGithub")}
                </Button>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  {t("github.mitGithubHinweis")}
                </Typography>
                {genanntesKonto !== null ? (
                  <Box
                    sx={{
                      mt: 2,
                      display: "flex",
                      gap: 1.5,
                      alignItems: "center",
                      flexWrap: "wrap",
                    }}
                  >
                    <Typography variant="body2">
                      <Trans
                        i18nKey="github.genanntesKonto"
                        values={{ login: genanntesKonto }}
                        components={{ 1: <strong /> }}
                      />
                    </Typography>
                    <Button
                      variant="text"
                      size="small"
                      onClick={() => void run(async () => ({ ok: await trenne() }))}
                      disabled={running}
                      sx={{ px: 0 }}
                    >
                      {t("github.anderesKonto")}
                    </Button>
                  </Box>
                ) : null}

                <Button
                  variant="text"
                  size="small"
                  onClick={() => setVonHand((offen) => !offen)}
                  sx={{ mt: 1.5, px: 0, display: "block" }}
                >
                  {vonHand ? t("github.vonHandVerbergen") : t("github.vonHand")}
                </Button>
              </Box>
            ) : null}

            {/* Ohne eingerichtete Anmeldung ist der Weg von Hand kein
                Nebenweg, sondern der Weg — dann ist nichts aufzuklappen. */}
            {!mitAnmeldung || vonHand ? (
              <Box>
                {mitAnmeldung ? <Divider sx={{ mb: 2.5 }} /> : null}

                {current?.login == null ? (
                  <Box
                    component="form"
                    onSubmit={(event) => {
                      event.preventDefault();
                      void run(() => nenneKonto(login));
                    }}
                    sx={{
                      display: "flex",
                      flexDirection: "column",
                      gap: 2,
                      alignItems: "flex-start",
                    }}
                  >
                    <Typography variant="subtitle2">
                      {t("github.kontoNennen")}
                    </Typography>
                    <TextField
                      label={t("github.benutzername")}
                      helperText={t("github.benutzernameHinweis")}
                      placeholder="anna"
                      value={login}
                      onChange={(e) => setLogin(e.target.value)}
                      slotProps={{ htmlInput: { maxLength: 39 } }}
                      required
                    />
                    <Button type="submit" variant="contained" disabled={running}>
                      {t("github.weiter")}
                    </Button>
                  </Box>
                ) : (
                  <>
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
                      {current.challenge_description}
                    </Box>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                      <Trans
                        i18nKey="github.nachweisErklaerung"
                        values={{ login: current.login }}
                        components={{ 1: <strong /> }}
                      />
                    </Typography>
                    <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
                      <Button
                        variant="contained"
                        onClick={() => void run(() => pruefeNachweis())}
                        disabled={running}
                      >
                        {running ? t("github.nachweisLaeuft") : t("github.nachweisPruefen")}
                      </Button>
                      {/* „Anderes Konto" steht oben, neben dem genannten —
                          zweimal derselbe Knopf wäre zweimal dieselbe Frage. */}
                      {mitAnmeldung ? null : (
                        <Button
                          variant="text"
                          onClick={() =>
                            void run(async () => ({ ok: await trenne() }))
                          }
                          disabled={running}
                        >
                          {t("github.anderesKonto")}
                        </Button>
                      )}
                    </Box>
                  </>
                )}
              </Box>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      {current !== null && current.verified ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              {current.login}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              {current.fetched_at !== null
                ? t("github.standVom", {
                    zeitpunkt: new Date(current.fetched_at).toLocaleString(
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

            {/* ADR-0022 §3: eine gekuerzte Menge sagt, dass sie gekuerzt ist. */}
            {!current.languages_complete && current.repositories.length > 0 ? (
              <Alert severity="info" sx={{ mb: 2 }}>
                {t("github.sprachenUnvollstaendig")}
              </Alert>
            ) : null}

            {current.repositories.length === 0 ? (
              <Typography sx={{ mb: 2 }}>
                {t("github.keineRepos")}
              </Typography>
            ) : (
              <Box component="ul" sx={{ pl: 2.5, mb: 2 }}>
                {current.repositories.map((repo) => (
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

                    {/*
                      WAS DIESES REPOSITORY BENUTZT — Topics zuerst, weil sie
                      eine Nennung sind: „kubernetes" hat der Besitzer selbst
                      dorthin geschrieben. Die Sprachen stehen daneben als
                      Menge, ohne Anteil: aus dem Byteverhältnis rechnete das
                      gelöschte Paket sein „Können" (ADR-0022 §2).

                      Beides ist eine Aussage über ein REPOSITORY, nie über
                      einen Menschen. Wer daraus eine Fähigkeit machen will,
                      übernimmt sie mit einem Klick ins Profil — dann hat sie
                      ein Mensch gesagt.
                    */}
                    {[...new Set([...repo.topics, ...repo.languages])].length >
                    0 ? (
                      <Box
                        sx={{
                          display: "flex",
                          flexWrap: "wrap",
                          gap: 0.5,
                          mt: 0.5,
                        }}
                      >
                        {[...new Set([...repo.topics, ...repo.languages])].map(
                          (wort) => (
                            <Chip key={wort} label={wort} size="small" />
                          ),
                        )}
                      </Box>
                    ) : null}
                  </Box>
                ))}
              </Box>
            )}

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap", alignItems: "center" }}>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={uebernehmen}
                    onChange={(_, an) => setUebernehmen(an)}
                  />
                }
                label={t("github.faehigkeitenUebernehmen")}
              />
              <Button
                variant="outlined"
                onClick={() =>
                  void run(async () => {
                    const result = await holeNeu();
                    if (!result.ok || !uebernehmen || !result.value) return result;
                    const profil = await getMyProfile();
                    if (!profil.ok || profil.profile === null) return result;
                    const extra = vorschlaegeAus(
                      ausRepositories(result.value.repositories),
                      profil.profile.skills,
                    );
                    if (extra.length === 0) return result;
                    await saveMyProfile({
                      headline: profil.profile.headline,
                      bio: profil.profile.bio,
                      location: profil.profile.location,
                      remote_ok: profil.profile.remote_ok,
                      skills: [...profil.profile.skills, ...extra].slice(0, 30),
                    });
                    return result;
                  })
                }
                disabled={running}
              >
                {running
                  ? t("github.aktualisierenLaeuft")
                  : t("github.aktualisieren")}
              </Button>
              <Button
                variant="text"
                onClick={() =>
                  void run(async () => ({ ok: await trenne() }))
                }
                disabled={running}
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
