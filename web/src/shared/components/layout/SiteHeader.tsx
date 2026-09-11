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
import ListItemIcon from "@mui/material/ListItemIcon";
import PersonOutlineIcon from "@mui/icons-material/PersonOutlined";
import DescriptionOutlinedIcon from "@mui/icons-material/DescriptionOutlined";
import CollectionsOutlinedIcon from "@mui/icons-material/CollectionsOutlined";
import GitHubIcon from "@mui/icons-material/GitHub";
import SendOutlinedIcon from "@mui/icons-material/SendOutlined";
import EditNoteOutlinedIcon from "@mui/icons-material/EditNoteOutlined";
import VerifiedUserOutlinedIcon from "@mui/icons-material/VerifiedUserOutlined";
import FolderSharedOutlinedIcon from "@mui/icons-material/FolderSharedOutlined";
import SettingsOutlinedIcon from "@mui/icons-material/SettingsOutlined";
import LogoutOutlinedIcon from "@mui/icons-material/LogoutOutlined";

import LoginOutlinedIcon from "@mui/icons-material/LoginOutlined";
import PersonAddOutlinedIcon from "@mui/icons-material/PersonAddOutlined";
import MenuList from "@mui/material/MenuList";
import Popover from "@mui/material/Popover";

import { useVorlieben } from "./darstellung";
import { Vorliebenebene, Vorliebenzeile, type Menueebene } from "./Vorliebenmenue";
import { ProfilAvatar } from "../ui";
import { zeigtGitHub } from "../../lib/berufsfelder";

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
  // Der Berater (ADR-0037). Er steht bei den Wegen der PERSON: das Mandat
  // gehoert ihr, nicht dem Unternehmen, fuer das sie gerade handelt.
  { pfad: "/advisor", schluessel: "kopf.berater", immer: false },
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

          {signedIn ? (
            <>
              <CompanySwitcher />
              {alsFirma ? <FirmenMenu /> : null}
              <AccountMenu />
            </>
          ) : (
            <BesucherMenu />
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

/** Welche Ebene des Menüs offen ist. Beim Schliessen zurück auf „haupt". */
function useVorliebenansicht(setAnker: (wert: null) => void) {
  const [ebene, setEbene] = useState<Menueebene>("haupt");
  const vorlieben = useVorlieben();

  return {
    ansicht: vorlieben,
    ebene,
    setzeAnsicht: setEbene,
    schliessen: () => {
      setAnker(null);
      setEbene("haupt");
    },
  };
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

  // Das Menü folgt dem Berufsfeld (ADR-0039). Es verbirgt nur — die Route
  // bleibt erreichbar, und ohne Berufsfeld bleibt der Eintrag stehen.
  const berufsfeld = useAppSelector(
    (state) => state.auth.session?.berufsfeld ?? null,
  );
  const github = zeigtGitHub(berufsfeld);

  const email = useAppSelector((state) => state.auth.session?.email ?? "");
  const { ansicht, ebene, setzeAnsicht, schliessen } = useVorliebenansicht(setAnker);

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
      <Popover
        anchorEl={anker}
        open={anker !== null}
        onClose={schliessen}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
        slotProps={{ paper: { sx: { width: 312, mt: 1 } } }}
      >
        <Box sx={{ px: 2, py: 1.25 }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 620 }} noWrap>
            {name}
          </Typography>
          {email ? (
            <Typography variant="caption" color="text.secondary" noWrap component="div">
              {email}
            </Typography>
          ) : null}
        </Box>
        <Divider />

        {ebene !== "haupt" ? (
          <MenuList sx={{ py: 0.5 }}>
            <Vorliebenebene
              ebene={ebene}
              darstellung={ansicht.darstellung}
              sprache={ansicht.sprache}
              onZurueck={() => setzeAnsicht("haupt")}
              onFertig={schliessen}
            />
          </MenuList>
        ) : (
        <MenuList sx={{ py: 0.5 }}>
        <Eintrag to="/profile" icon={<PersonOutlineIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.profil")}
        </Eintrag>
        <Eintrag to="/resume" icon={<DescriptionOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.lebenslauf")}
        </Eintrag>
        <Eintrag to="/portfolio" icon={<CollectionsOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.arbeiten")}
        </Eintrag>
        {github ? (
          <Eintrag to="/github" icon={<GitHubIcon fontSize="small" />} onFertig={schliessen}>
            {t("kopf.github")}
          </Eintrag>
        ) : null}
        <Eintrag to="/applications" icon={<SendOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.bewerbungen")}
        </Eintrag>
        <Eintrag to="/applications/drafts" icon={<EditNoteOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.entwuerfe")}
        </Eintrag>

        <Divider />
        <Eintrag to="/consents" icon={<VerifiedUserOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.freigaben")}
        </Eintrag>
        <Eintrag to="/my-data" icon={<FolderSharedOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.meineDaten")}
        </Eintrag>
        <Eintrag to="/settings" icon={<SettingsOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.einstellungen")}
        </Eintrag>

        <Divider />
        <Vorliebenzeile
          vorliebe={ansicht.darstellung}
          onOeffnen={() => setzeAnsicht("darstellung")}
        />
        <Vorliebenzeile
          vorliebe={ansicht.sprache}
          onOeffnen={() => setzeAnsicht("sprache")}
        />

        {/* „Konto löschen" steht in den Einstellungen und auf „Meine Daten",
            nicht hier neben „Abmelden". */}
        <Divider />
        <Eintrag to="/logout" icon={<LogoutOutlinedIcon fontSize="small" />} onFertig={schliessen}>
          {t("kopf.abmelden")}
        </Eintrag>
        </MenuList>
        )}
      </Popover>
    </>
  );
}

/**
 * Das Nutzersymbol für abgemeldete Besucher — an derselben Stelle wie der
 * Avatar, mit Anmelden, Registrieren und den Vorlieben.
 */
function BesucherMenu() {
  const { t } = useTranslation();
  const [anker, setAnker] = useState<null | HTMLElement>(null);
  const { ansicht, ebene, setzeAnsicht, schliessen } = useVorliebenansicht(setAnker);

  return (
    <>
      <Tooltip title={t("kopf.besucherMenue")}>
        <IconButton
          onClick={(event) => setAnker(event.currentTarget)}
          aria-haspopup="menu"
          aria-expanded={anker !== null}
          aria-label={t("kopf.besucherMenue")}
          size="small"
          sx={{ ml: 0.5, p: 0.25, color: "text.secondary" }}
        >
          <AccountCircleIcon sx={{ fontSize: 32 }} />
        </IconButton>
      </Tooltip>

      <Popover
        anchorEl={anker}
        open={anker !== null}
        onClose={schliessen}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
        slotProps={{ paper: { sx: { width: 288, mt: 1 } } }}
      >
        {ebene !== "haupt" ? (
          <MenuList sx={{ py: 0.5 }}>
            <Vorliebenebene
              ebene={ebene}
              darstellung={ansicht.darstellung}
              sprache={ansicht.sprache}
              onZurueck={() => setzeAnsicht("haupt")}
              onFertig={schliessen}
            />
          </MenuList>
        ) : (
          <MenuList sx={{ py: 0.5 }}>
            <Eintrag
              to="/login"
              icon={<LoginOutlinedIcon fontSize="small" />}
              onFertig={schliessen}
            >
              {t("kopf.anmelden")}
            </Eintrag>
            <Eintrag
              to="/register"
              icon={<PersonAddOutlinedIcon fontSize="small" />}
              onFertig={schliessen}
            >
              {t("kopf.registrieren")}
            </Eintrag>

            <Divider />
            <Vorliebenzeile
              vorliebe={ansicht.darstellung}
              onOeffnen={() => setzeAnsicht("darstellung")}
            />
            <Vorliebenzeile
              vorliebe={ansicht.sprache}
              onOeffnen={() => setzeAnsicht("sprache")}
            />
          </MenuList>
        )}
      </Popover>
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
        <Eintrag to="/scout">{t("kopf.kandidaten")}</Eintrag>
        <Eintrag to="/company/jobs">{t("kopf.unsereStellen")}</Eintrag>
        <Eintrag to="/company/applications">{t("kopf.unsereBewerbungen")}</Eintrag>
        <Eintrag to="/company/transfers">{t("kopf.unsereGespraeche")}</Eintrag>
        <Eintrag to="/company/advisor">{t("kopf.unsereBerater")}</Eintrag>
        <Divider />
        <Eintrag to="/company/profile">{t("kopf.firmenprofil")}</Eintrag>
        <Eintrag to="/company/team">{t("kopf.team")}</Eintrag>
      </Menu>
    </>
  );
}

/** Eine Zeile im Kontomenü. */
function Eintrag({
  to,
  icon,
  onFertig,
  children,
}: {
  to: string;
  icon?: React.ReactNode;
  onFertig?: () => void;
  children: React.ReactNode;
}) {
  return (
    <MenuItem
      component={RouterLink}
      to={to}
      onClick={onFertig}
    >
      {icon ? <ListItemIcon>{icon}</ListItemIcon> : null}
      {children}
    </MenuItem>
  );
}
