import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link as RouterLink, useLocation } from "react-router-dom";
import AppBar from "@mui/material/AppBar";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Container from "@mui/material/Container";
import Divider from "@mui/material/Divider";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Toolbar from "@mui/material/Toolbar";
import Typography from "@mui/material/Typography";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";

import { CompanySwitcher } from "./CompanySwitcher";
import { useAppSelector } from "../../../core/store/hooks";

/**
 * Die Kopfzeile.
 *
 * <strong>Abgemeldet steht hier GENAU EIN Zugang: „Anmelden".</strong> Vorher
 * standen dort zwei Einträge, und jeder blendete sich auf seiner eigenen Seite
 * aus — auf `/login` sah man nur „Registrieren", auf `/register` nur
 * „Anmelden", sonst beide. Der Kopf änderte damit seinen Inhalt beim Navigieren,
 * und das ist genau die Bewegung, die eine Oberfläche unzuverlässig wirken
 * lässt: die Ankerpunkte müssen liegen bleiben.
 *
 * Der Wechsel zwischen Anmelden und Registrieren gehört auf die Seite selbst,
 * wo die Person schon entschieden hat, sich anzumelden — dort ist es eine
 * Auswahl und keine zweite Werbefläche.
 *
 * <strong>Die Kopfzeile verbirgt nur, sie schützt nicht.</strong> Ob jemand
 * etwas darf, entscheidet der Server je Anfrage; wer hier einen Eintrag
 * versteckt, hat nichts gesichert. Die Firmeneinträge sind deshalb eine
 * Bequemlichkeit, keine Zugriffskontrolle.
 */
export function SiteHeader() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const signedIn = status === "authenticated";
  const alsFirma = session?.tenantId != null;

  return (
    <AppBar
      position="sticky"
      elevation={0}
      color="transparent"
      sx={{
        backdropFilter: "saturate(180%) blur(8px)",
        backgroundColor: (theme) =>
          theme.palette.mode === "light" ? "rgb(255 255 255 / 0.82)" : "rgb(18 19 28 / 0.82)",
        borderBottom: 1,
        borderColor: "divider",
      }}
    >
      {/* Das Randmass steht in allen drei Hüllen gleich (Kopf, Inhalt, Fuss).
          Vorher trug jede ihr eigenes, und der Anmeldeknopf klebte rechts am
          Rand — bei einer Kopfzeile fällt das am stärksten auf, weil sie auf
          jeder Seite steht. */}
      <Container maxWidth="lg" sx={{ px: { xs: 2, sm: 3, md: 4 } }}>
        <Toolbar disableGutters sx={{ gap: 2, minHeight: { xs: 60, md: 66 } }}>
          <Typography
            component={RouterLink}
            to="/"
            sx={{
              fontWeight: 700,
              fontSize: "1.0625rem",
              letterSpacing: "-0.02em",
              color: "text.primary",
              textDecoration: "none",
              flexShrink: 0,
            }}
          >
            worker<Box component="span" sx={{ color: "primary.main" }}>transfer</Box>
          </Typography>

          <Box sx={{ display: { xs: "none", md: "flex" }, gap: 0.5 }}>
            <NavLink to="/jobs" current={pathname}>
              Stellen
            </NavLink>
            {signedIn ? (
              <>
                <NavLink to="/overview" current={pathname}>
                  Übersicht
                </NavLink>
                <NavLink to="/market" current={pathname}>
                  Marktstatus
                </NavLink>
                <NavLink to="/transfers" current={pathname}>
                  Gespräche
                </NavLink>
              </>
            ) : null}
          </Box>

          <Box sx={{ flexGrow: 1 }} />

          {signedIn ? (
            <>
              <CompanySwitcher />
              {alsFirma ? <FirmenMenu /> : null}
              <AccountMenu />
            </>
          ) : (
            // EIN Zugang, immer sichtbar — auch auf /login und /register.
            <Button
              component={RouterLink}
              to="/login"
              variant="contained"
              sx={{ ml: 1, px: 2.5, flexShrink: 0 }}
            >
              {t("kopf.anmelden")}
            </Button>
          )}
        </Toolbar>
      </Container>
    </AppBar>
  );
}

function NavLink({
  to,
  current,
  children,
}: {
  to: string;
  current: string;
  children: React.ReactNode;
}) {
  // `startsWith` statt Gleichheit, damit auch /jobs/123 den Eintrag markiert:
  // eine Unterseite gehört sichtbar zu ihrem Bereich.
  const aktiv = current === to || current.startsWith(`${to}/`);

  return (
    <Button
      component={RouterLink}
      to={to}
      size="small"
      aria-current={aktiv ? "page" : undefined}
      sx={{
        color: aktiv ? "primary.main" : "text.secondary",
        backgroundColor: aktiv ? "action.selected" : "transparent",
        fontWeight: aktiv ? 620 : 560,
        "&:hover": { color: "text.primary" },
      }}
    >
      {children}
    </Button>
  );
}

/**
 * Das eigene Konto.
 *
 * <strong>Getrennt vom Firmenmenü, und das ist keine Ordnungsliebe.</strong>
 * Hier stand beides zusammen, durch einen Trennstrich geschieden — kompakter,
 * aber es verwischt genau die Grenze, um die sich diese Anwendung dreht: was
 * jemand als PERSON tut, und was er FÜR EIN UNTERNEHMEN tut. Wer sein Profil
 * freigibt, tut etwas anderes als wer Kandidatinnen ansieht, und die beiden
 * gehören nicht in dieselbe Liste.
 */
function AccountMenu() {
  const { t } = useTranslation();
  const [anker, setAnker] = useState<null | HTMLElement>(null);

  return (
    <>
      <Button
        size="small"
        endIcon={<ExpandMoreIcon />}
        onClick={(event) => setAnker(event.currentTarget)}
        aria-haspopup="menu"
        aria-expanded={anker !== null}
        sx={{ color: "text.secondary" }}
      >
        Mein Konto
      </Button>
      <Menu
        anchorEl={anker}
        open={anker !== null}
        onClose={() => setAnker(null)}
        onClick={() => setAnker(null)}
        slotProps={{ paper: { sx: { minWidth: 232, mt: 1 } } }}
      >
        <Eintrag to="/profile">{t("kopf.profil")}</Eintrag>
        <Eintrag to="/resume">{t("kopf.lebenslauf")}</Eintrag>
        <Eintrag to="/portfolio">{t("kopf.arbeiten")}</Eintrag>
        <Eintrag to="/github">{t("kopf.github")}</Eintrag>
        <Eintrag to="/applications">{t("kopf.bewerbungen")}</Eintrag>
        <Divider />
        <Eintrag to="/consents">{t("kopf.freigaben")}</Eintrag>
        <Eintrag to="/my-data">{t("kopf.meineDaten")}</Eintrag>
        <Eintrag to="/settings">{t("kopf.einstellungen")}</Eintrag>

        <Divider />
        <Eintrag to="/delete-account">{t("kopf.kontoLoeschen")}</Eintrag>
        <Eintrag to="/logout">{t("kopf.abmelden")}</Eintrag>
      </Menu>
    </>
  );
}

/**
 * Was für ein Unternehmen getan wird — nur sichtbar, während dafür gehandelt
 * wird.
 *
 * <strong>Es verbirgt, es schützt nicht.</strong> Ob jemand Kandidatinnen sehen
 * oder eine Stelle veröffentlichen darf, entscheidet der Server je Anfrage. Wer
 * die Adresse direkt tippt, bekommt dieselbe Antwort wie über dieses Menü.
 */
function FirmenMenu() {
  const { t } = useTranslation();
  const [anker, setAnker] = useState<null | HTMLElement>(null);

  return (
    <>
      <Button
        size="small"
        endIcon={<ExpandMoreIcon />}
        onClick={(event) => setAnker(event.currentTarget)}
        aria-haspopup="menu"
        aria-expanded={anker !== null}
        sx={{ color: "text.secondary" }}
      >
        Unternehmen
      </Button>
      <Menu
        anchorEl={anker}
        open={anker !== null}
        onClose={() => setAnker(null)}
        onClick={() => setAnker(null)}
        slotProps={{ paper: { sx: { minWidth: 232, mt: 1 } } }}
      >
        <Eintrag to="/candidates">{t("kopf.kandidaten")}</Eintrag>
        <Eintrag to="/company/jobs">{t("kopf.unsereStellen")}</Eintrag>
        <Eintrag to="/company/transfers">{t("kopf.unsereGespraeche")}</Eintrag>
        <Divider />
        <Eintrag to="/company/profile">{t("kopf.firmenprofil")}</Eintrag>
        <Eintrag to="/company/team">{t("kopf.team")}</Eintrag>
      </Menu>
    </>
  );
}

function Eintrag({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <MenuItem component={RouterLink} to={to}>
      {children}
    </MenuItem>
  );
}
