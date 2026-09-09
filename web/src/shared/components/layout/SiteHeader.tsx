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
import AccountCircleIcon from "@mui/icons-material/AccountCircle";
import MenuIcon from "@mui/icons-material/Menu";
import Drawer from "@mui/material/Drawer";
import IconButton from "@mui/material/IconButton";
import List from "@mui/material/List";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemText from "@mui/material/ListItemText";
import Tooltip from "@mui/material/Tooltip";
import { SettingsMenu } from "./SettingsMenu";
import { ProfilAvatar } from "../ui";

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
/**
 * Die Wege der Kopfzeile — EINMAL beschrieben.
 *
 * Kopfleiste und Schublade zeigen dieselben Einträge; zweimal geschrieben gehen
 * sie beim nächsten neuen Weg auseinander, und zwar an der Stelle, die niemand
 * ansieht (die schmale).
 */
const WEGE: { pfad: string; schluessel: string; immer: boolean }[] = [
  { pfad: "/jobs", schluessel: "kopf.stellen", immer: true },
  { pfad: "/overview", schluessel: "kopf.uebersicht", immer: false },
  { pfad: "/market", schluessel: "kopf.marktstatus", immer: false },
  { pfad: "/transfers", schluessel: "kopf.gespraeche", immer: false },
];

export function SiteHeader() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const [schublade, setSchublade] = useState(false);
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
          {/* DIE NAVIGATION VERSCHWAND ERSATZLOS.
              Bei 420px stand oben nur noch „workertransfer" und „Anmelden" —
              `Stellen` lag hinter `display: { xs: "none", md: "flex" }` und war
              von einem Telefon aus gar nicht erreichbar. Verstecken ohne Ersatz
              ist kein responsives Verhalten, sondern eine fehlende Seite. */}
          <IconButton
            onClick={() => setSchublade(true)}
            aria-label={t("kopf.menueOeffnen")}
            size="small"
            edge="start"
            sx={{ display: { xs: "inline-flex", md: "none" }, mr: 0.5 }}
          >
            <MenuIcon fontSize="small" />
          </IconButton>

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
            {WEGE.filter((weg) => weg.immer || signedIn).map((weg) => (
              <NavLink key={weg.pfad} to={weg.pfad} current={pathname}>
                {t(weg.schluessel)}
              </NavLink>
            ))}
          </Box>

          <Box sx={{ flexGrow: 1 }} />

          <SettingsMenu />

          {signedIn ? (
            <>
              <CompanySwitcher />
              {alsFirma ? <FirmenMenu /> : null}
              <AccountMenu />
            </>
          ) : (
            // EIN Zugang, immer sichtbar — auch auf /login und /register.
            /* EIN NUTZERSYMBOL, auf JEDER Breite, und kein Knopf daneben.
               Vorher stand schmal ein Symbol und breit zusätzlich ein Knopf mit
               Text — zwei Bedienelemente für dieselbe Handlung, und beim Ziehen
               des Fensters erschien und verschwand eines davon. An genau der
               Stelle steht angemeldet der Avatar; ob ein Konto offen ist oder
               nicht, darf die Stelle nicht wandern. */
            <Tooltip title={t("kopf.anmelden")}>
              <IconButton
                component={RouterLink}
                to="/login"
                aria-label={t("kopf.anmelden")}
                size="small"
                sx={{ ml: 0.5, p: 0.25, color: "text.secondary" }}
              >
                <AccountCircleIcon sx={{ fontSize: 32 }} />
              </IconButton>
            </Tooltip>
          )}
        </Toolbar>
      </Container>

      <Drawer
        anchor="left"
        open={schublade}
        onClose={() => setSchublade(false)}
        slotProps={{ paper: { sx: { width: 268 } } }}
      >
        <Box sx={{ px: 2, pt: 2.5, pb: 1 }}>
          <Typography
            variant="caption"
            sx={{ fontWeight: 660, letterSpacing: "0.06em", textTransform: "uppercase" }}
            color="text.secondary"
          >
            {t("kopf.navigation")}
          </Typography>
        </Box>
        <List sx={{ px: 1 }}>
          {WEGE.filter((weg) => weg.immer || signedIn).map((weg) => (
            <ListItemButton
              key={weg.pfad}
              component={RouterLink}
              to={weg.pfad}
              selected={pathname === weg.pfad}
              onClick={() => setSchublade(false)}
            >
              <ListItemText primary={t(weg.schluessel)} />
            </ListItemButton>
          ))}
        </List>
      </Drawer>
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
  const name = useAppSelector((state) => state.auth.session?.displayName ?? "");

  return (
    <>
      {/* DAS BILD DER PERSON, nicht ein Knopf mit dem Wort „Mein Konto".
          Solange niemand ein Foto hochgeladen hat, stehen dort die Initialen
          auf einer Farbe, die dem Namen folgt — beständig über Geräte hinweg,
          sonst wäre sie kein Erkennungsmerkmal. Kommt das Foto, füllt es
          `src` und sonst ändert sich nichts. */}
      <Tooltip title={t("kopf.meinKonto")}>
        <IconButton
          onClick={(event) => setAnker(event.currentTarget)}
          aria-haspopup="menu"
          aria-expanded={anker !== null}
          aria-label={t("kopf.meinKonto")}
          size="small"
          sx={{ ml: 0.5, p: 0.25 }}
        >
          <ProfilAvatar name={name} size={32} />
        </IconButton>
      </Tooltip>
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
        <Eintrag to="/applications/drafts">{t("kopf.entwuerfe")}</Eintrag>
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
        <Eintrag to="/company/applications">{t("kopf.unsereBewerbungen")}</Eintrag>
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
