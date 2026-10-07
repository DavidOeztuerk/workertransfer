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

import { usePreferences } from "./appearance";
import { PreferenceLevel, PreferenceRow, type MenuLevel } from "./PreferencesMenu";
import { ProfileAvatar } from "../ui";
import { offersGitHub } from "../../lib/occupationalFields";

/**
 * The routes of the header — described ONCE.
 *
 * The header bar and the drawer show the same entries; written twice they
 * drift apart at the next new route, and at the place nobody looks at (the
 * narrow one).
 */
const ROUTES: { path: string; labelKey: string; always: boolean }[] = [
  { path: "/jobs", labelKey: "header.jobs", always: true },
  { path: "/overview", labelKey: "header.overview", always: false },
  { path: "/market", labelKey: "header.marketStatus", always: false },
  { path: "/transfers", labelKey: "header.conversations", always: false },
  // The advisor (ADR-0037). It stands among the PERSON's routes: the mandate
  // belongs to her, not to the company she is acting for right now.
  { path: "/advisor", labelKey: "header.advisor", always: false },
  // The work sample (ADR-0042). It too stands among the PERSON's routes: the
  // evaluation belongs to the case, and whoever it is about may read it.
  { path: "/assessments", labelKey: "header.assessments", always: false },
];

/**
 * The header.
 *
 * <strong>Signed out, EXACTLY ONE entrance stands here: "Anmelden".</strong>
 * There used to be two entries, and each hid itself on its own page — on
 * `/login` one saw only "Registrieren", on `/register` only "Anmelden",
 * elsewhere both. The header thereby changed its content while navigating, and
 * that is exactly the movement that makes a UI feel unreliable: the anchor
 * points must stay put.
 *
 * Switching between signing in and registering belongs on the page itself,
 * where the person has already decided to sign in — there it is a choice and
 * not a second advertising space.
 *
 * <strong>The header only hides, it does not protect.</strong> Whether somebody
 * may do something is decided by the server per request; whoever hides an
 * entry here has secured nothing. The company entries are therefore a
 * convenience, not access control.
 */

export function SiteHeader() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const signedIn = status === "authenticated";
  const actingForCompany = session?.tenantId != null;

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
      <Container
        maxWidth="lg"
        // The same gutter in all three shells (header, content, footer): with
        // its own, the sign-in button sticks to the right edge — most visible in
        // a header, because it stands on every page.
        sx={{ px: { xs: 2, sm: 3, md: 4 } }}
      >
        <Toolbar disableGutters sx={{ gap: 2, minHeight: { xs: 60, md: 66 } }}>
          <IconButton
            // The drawer is the narrow-screen replacement for the navigation
            // below, which hides behind `display: { xs: "none", md: "flex" }`.
            // Hiding without a replacement is not responsive behaviour but a
            // missing page.
            onClick={() => setDrawerOpen(true)}
            aria-label={t("header.openMenu")}
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
            {ROUTES.filter((route) => route.always || signedIn).map((route) => (
              <NavLink key={route.path} to={route.path} current={pathname}>
                {t(route.labelKey)}
              </NavLink>
            ))}
          </Box>

          <Box sx={{ flexGrow: 1 }} />

          {signedIn ? (
            <>
              <CompanySwitcher />
              {actingForCompany ? <CompanyMenu /> : null}
              <AccountMenu />
            </>
          ) : (
            <VisitorMenu />
          )}
        </Toolbar>
      </Container>

      <Drawer
        anchor="left"
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        slotProps={{ paper: { sx: { width: 268 } } }}
      >
        <Box sx={{ px: 2, pt: 2.5, pb: 1 }}>
          <Typography
            variant="caption"
            sx={{ fontWeight: 660, letterSpacing: "0.06em", textTransform: "uppercase" }}
            color="text.secondary"
          >
            {t("header.navigation")}
          </Typography>
        </Box>
        <List sx={{ px: 1 }}>
          {ROUTES.filter((route) => route.always || signedIn).map((route) => (
            <ListItemButton
              key={route.path}
              component={RouterLink}
              to={route.path}
              selected={pathname === route.path}
              onClick={() => setDrawerOpen(false)}
            >
              <ListItemText primary={t(route.labelKey)} />
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
  // `startsWith` instead of equality, so that /jobs/123 marks the entry too: a
  // subpage visibly belongs to its area.
  const active = current === to || current.startsWith(`${to}/`);

  return (
    <Button
      component={RouterLink}
      to={to}
      size="small"
      aria-current={active ? "page" : undefined}
      sx={{
        color: active ? "primary.main" : "text.secondary",
        backgroundColor: active ? "action.selected" : "transparent",
        fontWeight: active ? 620 : 560,
        "&:hover": { color: "text.primary" },
      }}
    >
      {children}
    </Button>
  );
}

/** Which level of the menu is open. Closing returns to "main". */
function usePreferenceView(setAnchor: (value: null) => void) {
  const [level, setLevel] = useState<MenuLevel>("main");
  const preferences = usePreferences();

  return {
    preferences,
    level,
    setLevel,
    close: () => {
      setAnchor(null);
      setLevel("main");
    },
  };
}

/**
 * One's own account.
 *
 * <strong>Separate from the company menu, and that is not tidiness.</strong>
 * Both used to stand together, parted by a divider — more compact, but it
 * blurs exactly the boundary this application turns on: what somebody does as
 * a PERSON, and what they do FOR A COMPANY. Whoever releases their profile does
 * something other than whoever looks at candidates, and the two do not belong
 * in the same list.
 */
function AccountMenu() {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const name = useAppSelector((state) => state.auth.session?.displayName ?? "");

  // The menu follows the occupational field (ADR-0039). It only hides — the
  // route stays reachable, and without an occupational field the entry stays.
  const occupationalField = useAppSelector(
    (state) => state.auth.session?.occupationalField ?? null,
  );
  const github = offersGitHub(occupationalField);

  const email = useAppSelector((state) => state.auth.session?.email ?? "");
  const { preferences, level, setLevel, close } = usePreferenceView(setAnchor);

  return (
    <>
      <Tooltip title={t("header.myAccount")}>
        <IconButton
          onClick={(event) => setAnchor(event.currentTarget)}
          aria-haspopup="menu"
          aria-expanded={anchor !== null}
          aria-label={t("header.myAccount")}
          size="small"
          sx={{ ml: 0.5, p: 0.25 }}
        >
          <ProfileAvatar name={name} size={32} />
        </IconButton>
      </Tooltip>
      <Popover
        anchorEl={anchor}
        open={anchor !== null}
        onClose={close}
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

        {level !== "main" ? (
          <MenuList sx={{ py: 0.5 }}>
            <PreferenceLevel
              level={level}
              appearance={preferences.appearance}
              language={preferences.language}
              onBack={() => setLevel("main")}
              onDone={close}
            />
          </MenuList>
        ) : (
        <MenuList sx={{ py: 0.5 }}>
        <MenuEntry to="/profile" icon={<PersonOutlineIcon fontSize="small" />} onDone={close}>
          {t("header.profile")}
        </MenuEntry>
        <MenuEntry to="/resume" icon={<DescriptionOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.resume")}
        </MenuEntry>
        <MenuEntry to="/portfolio" icon={<CollectionsOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.portfolio")}
        </MenuEntry>
        {github ? (
          <MenuEntry to="/github" icon={<GitHubIcon fontSize="small" />} onDone={close}>
            {t("header.github")}
          </MenuEntry>
        ) : null}
        <MenuEntry to="/applications" icon={<SendOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.applications")}
        </MenuEntry>
        <MenuEntry to="/applications/drafts" icon={<EditNoteOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.drafts")}
        </MenuEntry>
        <MenuEntry to="/messages" icon={<SendOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.messages")}
        </MenuEntry>

        <Divider />
        <MenuEntry to="/consents" icon={<VerifiedUserOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.consents")}
        </MenuEntry>
        <MenuEntry to="/my-data" icon={<FolderSharedOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.myData")}
        </MenuEntry>
        <MenuEntry to="/settings" icon={<SettingsOutlinedIcon fontSize="small" />} onDone={close}>
          {t("header.settings")}
        </MenuEntry>

        <Divider />
        <PreferenceRow
          preference={preferences.appearance}
          onOpen={() => setLevel("appearance")}
        />
        <PreferenceRow
          preference={preferences.language}
          onOpen={() => setLevel("language")}
        />

        <Divider />
        <MenuEntry
          // "Delete account" lives in the settings and on "My data", never here
          // beside "Sign out".
          to="/logout"
          icon={<LogoutOutlinedIcon fontSize="small" />}
          onDone={close}
        >
          {t("header.signOut")}
        </MenuEntry>
        </MenuList>
        )}
      </Popover>
    </>
  );
}

/**
 * The user symbol for signed-out visitors — at the same place as the avatar,
 * with sign in, register and the preferences.
 */
function VisitorMenu() {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const { preferences, level, setLevel, close } = usePreferenceView(setAnchor);

  return (
    <>
      <Tooltip title={t("header.visitorMenu")}>
        <IconButton
          onClick={(event) => setAnchor(event.currentTarget)}
          aria-haspopup="menu"
          aria-expanded={anchor !== null}
          aria-label={t("header.visitorMenu")}
          size="small"
          sx={{ ml: 0.5, p: 0.25, color: "text.secondary" }}
        >
          <AccountCircleIcon sx={{ fontSize: 32 }} />
        </IconButton>
      </Tooltip>

      <Popover
        anchorEl={anchor}
        open={anchor !== null}
        onClose={close}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
        slotProps={{ paper: { sx: { width: 288, mt: 1 } } }}
      >
        {level !== "main" ? (
          <MenuList sx={{ py: 0.5 }}>
            <PreferenceLevel
              level={level}
              appearance={preferences.appearance}
              language={preferences.language}
              onBack={() => setLevel("main")}
              onDone={close}
            />
          </MenuList>
        ) : (
          <MenuList sx={{ py: 0.5 }}>
            <MenuEntry
              to="/login"
              icon={<LoginOutlinedIcon fontSize="small" />}
              onDone={close}
            >
              {t("header.signIn")}
            </MenuEntry>
            <MenuEntry
              to="/register"
              icon={<PersonAddOutlinedIcon fontSize="small" />}
              onDone={close}
            >
              {t("header.register")}
            </MenuEntry>

            <Divider />
            <PreferenceRow
              preference={preferences.appearance}
              onOpen={() => setLevel("appearance")}
            />
            <PreferenceRow
              preference={preferences.language}
              onOpen={() => setLevel("language")}
            />
          </MenuList>
        )}
      </Popover>
    </>
  );
}

/**
 * What is done for a company — visible only while acting for one.
 *
 * <strong>It hides, it does not protect.</strong> Whether somebody may see
 * candidates or publish a job is decided by the server per request. Whoever
 * types the address directly gets the same answer as through this menu.
 */
function CompanyMenu() {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);

  return (
    <>
      <Button
        size="small"
        endIcon={<ExpandMoreIcon />}
        onClick={(event) => setAnchor(event.currentTarget)}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        sx={{ color: "text.secondary" }}
      >
        {t("header.company")}
      </Button>
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => setAnchor(null)}
        onClick={() => setAnchor(null)}
        slotProps={{ paper: { sx: { minWidth: 232, mt: 1 } } }}
      >
        <MenuEntry to="/scout">{t("header.candidates")}</MenuEntry>
        <MenuEntry to="/company/jobs">{t("header.ourJobs")}</MenuEntry>
        <MenuEntry to="/company/applications">{t("header.receivedApplications")}</MenuEntry>
        <MenuEntry to="/messages">{t("header.messages")}</MenuEntry>
        <MenuEntry to="/company/transfers">{t("header.ourConversations")}</MenuEntry>
        <MenuEntry to="/company/advisor">{t("header.ourAdvisor")}</MenuEntry>
        <MenuEntry to="/company/assessments">{t("header.ourAssessments")}</MenuEntry>
        <Divider />
        <MenuEntry to="/company/profile">{t("header.companyProfile")}</MenuEntry>
        <MenuEntry to="/company/team">{t("header.team")}</MenuEntry>
      </Menu>
    </>
  );
}

/** One row in the account menu. */
function MenuEntry({
  to,
  icon,
  onDone,
  children,
}: {
  to: string;
  icon?: React.ReactNode;
  onDone?: () => void;
  children: React.ReactNode;
}) {
  return (
    <MenuItem
      component={RouterLink}
      to={to}
      onClick={onDone}
    >
      {icon ? <ListItemIcon>{icon}</ListItemIcon> : null}
      {children}
    </MenuItem>
  );
}
