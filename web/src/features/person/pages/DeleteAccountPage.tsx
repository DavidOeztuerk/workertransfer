import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { sitzungBeendet } from "../../auth/store/authSlice";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { loeschungVerlangen } from "../api/erasure";

/** Nur die Nummern — der Wortlaut liegt in den Katalogen. */
const WAS_VERSCHWINDET = [1, 2, 3, 4, 5, 6, 7, 8] as const;

function Abschnitt({
  titel,
  children,
}: {
  titel: string;
  children: React.ReactNode;
}) {
  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {titel}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

/**
 * <c>/delete-account</c> — wo das Versprechen einem Menschen gegeben wird.
 *
 * <strong>Alles Wichtige steht VOR dem Knopf</strong>, und der unangenehmste
 * Satz steht laut da: auch die Bewerbung, über die jemand <em>eingestellt</em>
 * wurde, verschwindet aus der Liste des Unternehmens. Eine Löschzusage, deren
 * Ausnahmen man erst hinterher erfährt, ist keine.
 *
 * <strong>Zwei Schritte, inline, kein Dialog.</strong> Ein Dialog nähme die
 * Seite weg, auf der gerade steht, was verschwindet — und genau das soll beim
 * zweiten Klick noch lesbar sein. Der zweite Knopf ist anders formuliert als
 * der erste, damit niemand zweimal dasselbe klickt.
 *
 * <strong>Kein getipptes Wort, keine Wartezeit, kein erneutes Passwort.</strong>
 * Wer löschen will, darf. Jede zusätzliche Hürde wäre eine Bremse gegen die
 * Person, nicht für sie.
 *
 * <strong>Die Falle, die hier schon zugeschnappt ist:</strong> bei Erfolg ist
 * die Sitzung weg, die Hülle zeichnet die Seite also ohne Prinzipal neu. Der
 * angenommene Zustand muss deshalb VOR der Anmeldeaufforderung geprüft werden —
 * sonst liest die Person direkt nach dem Löschen „Bitte anmelden“.
 */
export function DeleteAccountPage() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const status = useAppSelector((state) => state.auth.status);
  const firmen = useAppSelector((state) => state.auth.memberships);

  const [fragt, setFragt] = useState(false);
  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [angenommen, setAngenommen] = useState(false);

  // ZUERST. Siehe oben — nach dem Löschen gibt es keinen Prinzipal mehr.
  if (angenommen) {
    return (
      <PageShell title={t("loeschung.titel")} narrow>
        <Card>
          <CardContent>
            {/* `info` und nicht `error`: das ist eine Bestätigung. Ein roter
                Kasten hier läse sich, als sei etwas schiefgegangen. */}
            <Alert severity="info" sx={{ mb: 2 }} role="status">
              <strong>{t("loeschung.angenommenTitel")}</strong>{" "}
              <Trans
                i18nKey="loeschung.angenommenText"
                components={{ 1: <strong /> }}
              />
            </Alert>
            <Typography variant="body2" color="text.secondary">
              {t("loeschung.angenommenWarum")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (status !== "authenticated") {
    return (
      <AnmeldungNoetig
        titel={t("loeschung.titel")}
        satz="loeschung.anmelden"
      />
    );
  }

  async function loeschen() {
    setLaeuft(true);
    const ergebnis = await loeschungVerlangen();
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setAngenommen(true);

      // Der Server hat jede Sitzung schon widerrufen — der Speicher muss
      // nachziehen, sonst zeigt der Kopf weiter das Konto-Menü, während diese
      // Seite „Du bist abgemeldet" sagt. Zwei Aussagen über dieselbe Sache, und
      // die sichtbarere ist die falsche.
      //
      // NICHT `logout()`: das fragt den Server, und der antwortet auf eine
      // widerrufene Sitzung mit 401 — der Speicher bliebe stehen.
      dispatch(sitzungBeendet());
    } else {
      setFehler(ergebnis.error.detail);
    }
  }

  return (
    <PageShell
      title={t("loeschung.titel")}
      narrow
      lead={t("loeschung.lead")}
    >
      <Abschnitt titel={t("loeschung.wasTitel")}>
        <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
          {WAS_VERSCHWINDET.map((nummer) => (
            <Typography component="li" key={nummer} sx={{ mb: 0.5 }}>
              {t(`loeschung.was${nummer}`)}
            </Typography>
          ))}
        </Box>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="loeschung.nichtsBleibt"
            components={[<strong key="a" />, <strong key="b" />]}
          />
        </Typography>
      </Abschnitt>

      <Abschnitt titel={t("loeschung.bleibtTitel")}>
        <Typography variant="body2" color="text.secondary">
          <Trans i18nKey="loeschung.bleibtText" components={{ 1: <em /> }} />
        </Typography>
      </Abschnitt>

      {firmen.length > 0 ? (
        <Abschnitt titel={t("loeschung.firmenTitel")}>
          <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
            {firmen.map((firma) => (
              <Typography component="li" key={firma.id} sx={{ mb: 0.5 }}>
                <strong>{firma.name}</strong>{" "}
                {firma.role === "admin" ? (
                  <Trans
                    i18nKey="loeschung.firmaAdmin"
                    components={{ 1: <em />, 3: <strong /> }}
                  />
                ) : (
                  t("loeschung.firmaMitglied")
                )}
              </Typography>
            ))}
          </Box>
          <Typography variant="body2" color="text.secondary">
            <Trans
              i18nKey="loeschung.firmenHaeltNichtAuf"
              components={{ 1: <strong /> }}
            />
          </Typography>
        </Abschnitt>
      ) : null}

      <Abschnitt titel={t("loeschung.ablaufTitel")}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          <Trans
            i18nKey="loeschung.ablaufText"
            components={{ 1: <strong />, 3: <strong /> }}
          />
        </Typography>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="loeschung.ablaufExport"
            components={{ 1: <Link component={RouterLink} to="/my-data" /> }}
          />
        </Typography>
      </Abschnitt>

      <Card>
        <CardContent>
          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {fragt ? (
            <>
              <Alert severity="warning" sx={{ mb: 2 }}>
                {t("loeschung.letzteFrage")}
              </Alert>
              <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
                <Button
                  variant="contained"
                  color="error"
                  onClick={() => void loeschen()}
                  disabled={laeuft}
                >
                  {laeuft ? t("loeschung.laeuft") : t("loeschung.bestaetigen")}
                </Button>
                <Button
                  variant="text"
                  onClick={() => {
                    setFragt(false);
                    setFehler(null);
                  }}
                  disabled={laeuft}
                >
                  {t("allgemein.abbrechen")}
                </Button>
              </Box>
            </>
          ) : (
            <Button
              variant="outlined"
              color="error"
              onClick={() => setFragt(true)}
            >
              {t("loeschung.knopf")}
            </Button>
          )}
        </CardContent>
      </Card>
    </PageShell>
  );
}
