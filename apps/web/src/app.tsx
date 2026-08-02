import {
  Link,
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  useRouter,
} from "@tanstack/react-router";
import type { ReactNode } from "react";

import { Button } from "@workertransfer/ui";

import { useCompanies, useSwitchCompany } from "./auth/companies";
import { useLogout, useSession } from "./auth/session";
import { CandidatesRoute } from "./routes/candidates";
import { CompanyTransfersRoute } from "./routes/company-transfers";
import { ConsentsRoute } from "./routes/consents";
import { CompanyNewRoute } from "./routes/company-new";
import { HomeRoute } from "./routes/home";
import { OverviewRoute } from "./routes/overview";
import { LoginRoute } from "./routes/login";
import { PortfolioRoute } from "./routes/portfolio";
import { ProfileRoute } from "./routes/profile";
import { ResumeRoute } from "./routes/resume";
import { TeamRoute } from "./routes/team";
import { PendingRequestBadge } from "./resume/pending-badge";
import { ApplicationsRoute } from "./routes/applications";
import { CareerRoute } from "./routes/career";
import { CompanyJobsRoute } from "./routes/company-jobs";
import { CompanyProfileRoute } from "./routes/company-profile";
import { InvitationRoute } from "./routes/invitation";
import { JobsRoute } from "./routes/jobs";
import { MarketRoute } from "./routes/market";
import { MyDataRoute } from "./routes/my-data";
import { RegisterRoute } from "./routes/register";
import { SettingsRoute } from "./routes/settings";
import { TransfersRoute } from "./routes/transfers";
import { VerifyRoute } from "./routes/verify";

/**
 * Ein aufklappbares Menü in der Kopfzeile — als natives `<details>`.
 *
 * Kein eigenes Menü-Bauteil mit Tastatursteuerung, Fokusfalle und
 * Escape-Behandlung: `<details>` kann das alles schon, und ein selbstgebautes
 * Menü, das es NICHT kann, ist schlechter als eine Liste. Wenn `packages/ui`
 * eines Tages ein richtiges Menü hat, tritt es hier an die Stelle.
 *
 * Der Zähler steht an der Zusammenfassung, nicht am Eintrag darin: eine
 * Anfrage, die man erst nach dem Aufklappen sieht, erreicht niemanden.
 */
function HeaderMenu({
  label,
  badge,
  children,
}: {
  label: string;
  badge?: ReactNode;
  children: ReactNode;
}) {
  return (
    <details className="site-header__menu">
      <summary className="site-header__link">
        {label}
        {badge}
      </summary>
      <div className="site-header__menu-panel">{children}</div>
    </details>
  );
}

export function RootLayout() {
  const router = useRouter();
  const current = router.state.location.pathname;
  const { user, isLoading } = useSession();
  const logout = useLogout();

  const onHome = current === "/";

  return (
    <>
      {/* Ein Header für alle Seiten. Vorher trug die Startseite ihre eigene
          Kopfzeile im Hero, während diese hier als nackter Streifen darüber
          stand — zwei gestapelte Navigationen, eine davon ungestylt. Auf der
          Startseite liegt der Header jetzt transparent über dem Hero. */}
      <header className={`site-header${onHome ? " site-header--on-hero" : ""}`}>
        <a className="brand" href="/" aria-label="WorkerTransfer Startseite">
          worker<span>transfer</span>
        </a>
        <nav aria-label="Hauptnavigation">
          {/* Stellen sind öffentlich — der Link gehört nicht hinter die
              Anmeldung, sonst wäre er das Gegenteil dessen, was er verspricht. */}
          <Link className="site-header__link" to="/jobs">
            Stellen
          </Link>
          {/* Die Abschnittslinks lebten in der alten Hero-Kopfzeile und wären
              mit ihr verschwunden. Sie gehören nur auf die Startseite — auf
              /login zeigen sie ins Leere. */}
          {onHome ? (
            <>
              <a className="site-header__link" href="#principles">
                Prinzipien
              </a>
              <a className="site-header__link" href="#roadmap">
                Produkt
              </a>
            </>
          ) : null}
          {isLoading ? null : user !== null ? (
            <>
              <CompanySwitcher activeTenantId={user.tenant_id} />
              {/* Oben bleibt, was den Transfermarkt ausmacht — das ist der
                  Grund, aus dem jemand hier ist. Alles Verwaltende liegt eine
                  Ebene tiefer. Vorher standen siebzehn Einträge nebeneinander,
                  und siebzehn gleichwertige Einträge sind keine Navigation,
                  sondern eine Liste. */}
              <Link className="site-header__link" to="/markt">
                Marktstatus
              </Link>
              <Link className="site-header__link" to="/transfers">
                Gespräche
              </Link>
              <HeaderMenu label="Mein Konto" badge={<PendingRequestBadge />}>
                <Link className="site-header__menu-item" to="/profile">
                  Mein Profil
                </Link>
                <Link className="site-header__menu-item" to="/resume">
                  Lebenslauf
                </Link>
                <Link className="site-header__menu-item" to="/portfolio">
                  Arbeiten
                </Link>
                <Link className="site-header__menu-item" to="/applications">
                  Bewerbungen
                </Link>
                <Link className="site-header__menu-item" to="/freigaben">
                  Meine Freigaben
                </Link>
                <Link className="site-header__menu-item" to="/meine-daten">
                  Meine Daten
                </Link>
                <Link className="site-header__menu-item" to="/einstellungen">
                  Einstellungen
                </Link>
                <Link className="site-header__menu-item" to="/company/new">
                  Unternehmen anlegen
                </Link>
              </HeaderMenu>
              {user.tenant_id !== null ? (
                <HeaderMenu label="Unternehmen">
                  <Link className="site-header__menu-item" to="/candidates">
                    Kandidaten
                  </Link>
                  <Link className="site-header__menu-item" to="/company/transfers">
                    Transfers
                  </Link>
                  <Link className="site-header__menu-item" to="/company/jobs">
                    Unsere Stellen
                  </Link>
                  <Link className="site-header__menu-item" to="/company/profile">
                    Unser Unternehmen
                  </Link>
                  <Link className="site-header__menu-item" to="/company/team">
                    Mannschaft
                  </Link>
                </HeaderMenu>
              ) : null}
              <Button variant="quiet" onClick={() => logout.mutate()} disabled={logout.isPending}>
                {logout.isPending ? "Abmeldung läuft…" : "Abmelden"}
              </Button>
            </>
          ) : (
            <>
              {current !== "/login" ? (
                <Link className="site-header__link" to="/login">
                  Anmelden
                </Link>
              ) : null}
              {current !== "/register" ? (
                <Link className="site-header__cta" to="/register">
                  Registrieren
                </Link>
              ) : null}
            </>
          )}
        </nav>
      </header>
      <Outlet />
    </>
  );
}

function CompanySwitcher({ activeTenantId }: { activeTenantId: string | null }) {
  const { companies } = useCompanies();
  const switchTo = useSwitchCompany();
  if (companies.length === 0) return null;

  const active = companies.find((c) => c.id === activeTenantId);
  return (
    <label>
      Handeln als
      <select
        value={activeTenantId ?? ""}
        onChange={(e) => switchTo.mutate(e.target.value)}
        disabled={switchTo.isPending}
      >
        {/* "als Person" ist der Standardzustand — ein Unternehmen wird bewusst
            gewählt (ADR-0017). Zurückwechseln heißt neu anmelden, weil der
            Tenant im Token steckt und es keinen Endpunkt gibt, der ihn wieder
            entfernt. Solange eines aktiv ist, wäre die Option also eine Lüge:
            sie würde POST /auth/company/ auslösen und wortlos 404 liefern. */}
        <option value="" disabled={activeTenantId !== null}>
          {activeTenantId === null ? "Ich selbst" : "Ich selbst (Abmelden nötig)"}
        </option>
        {companies.map((company) => (
          <option key={company.id} value={company.id}>
            {company.name}
          </option>
        ))}
      </select>
      {active !== undefined ? <span>{active.role}</span> : null}
    </label>
  );
}

function ProfileWithSession() {
  const { user } = useSession();
  return <ProfileRoute principal={user} />;
}

function PortfolioWithSession() {
  const { user } = useSession();
  return <PortfolioRoute principal={user} />;
}

function ResumeWithSession() {
  const { user } = useSession();
  return <ResumeRoute principal={user} />;
}

function CandidatesWithSession() {
  const { user } = useSession();
  return <CandidatesRoute principal={user} />;
}

function MyDataWithSession() {
  const { user } = useSession();
  return <MyDataRoute principal={user} />;
}

function ConsentsWithSession() {
  const { user } = useSession();
  return <ConsentsRoute principal={user} />;
}

function SettingsWithSession() {
  const { user } = useSession();
  return <SettingsRoute principal={user} />;
}

function MarketWithSession() {
  const { user } = useSession();
  return <MarketRoute principal={user} />;
}

function TransfersWithSession() {
  const { user } = useSession();
  return <TransfersRoute principal={user} />;
}

function CompanyTransfersWithSession() {
  const { user } = useSession();
  return <CompanyTransfersRoute principal={user} />;
}

function ApplicationsWithSession() {
  const { user } = useSession();
  return <ApplicationsRoute principal={user} />;
}

function JobsWithSession() {
  // Die Seite braucht keine Anmeldung — der Prinzipal entscheidet nur, ob
  // „Bewerben" angeboten wird.
  const { user } = useSession();
  return <JobsRoute principal={user} />;
}

function CompanyProfileWithSession() {
  const { user } = useSession();
  return <CompanyProfileRoute principal={user} />;
}

function CompanyJobsWithSession() {
  const { user } = useSession();
  return <CompanyJobsRoute principal={user} />;
}

function TeamWithSession() {
  const { user } = useSession();
  return <TeamRoute principal={user} />;
}

function CompanyNewWithSession() {
  // The route needs the principal; the component takes it as a prop so it stays
  // testable without a live session.
  const { user } = useSession();
  return <CompanyNewRoute principal={user} />;
}

const rootRoute = createRootRoute({ component: RootLayout });
function HomeOrOverview() {
  // Angemeldet zeigt die Startseite, was ansteht — nicht mehr die Werbung.
  // Wer schon da ist, muss nicht überzeugt werden.
  const { user, isLoading } = useSession();
  if (isLoading) return null;
  return user === null ? <HomeRoute /> : <OverviewRoute principal={user} />;
}

const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: HomeOrOverview,
});
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/login",
  component: LoginRoute,
});

const registerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/register",
  component: RegisterRoute,
});
const verifyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/verify",
  component: VerifyRoute,
});
const profileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/profile",
  component: ProfileWithSession,
});
const portfolioRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/portfolio",
  component: PortfolioWithSession,
});
const resumeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/resume",
  component: ResumeWithSession,
});
const candidatesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/candidates",
  component: CandidatesWithSession,
});
const careerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/karriere/$slug",
  component: CareerRoute,
});
const jobsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/jobs",
  component: JobsWithSession,
});
const applicationsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/applications",
  component: ApplicationsWithSession,
});
const myDataRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/meine-daten",
  component: MyDataWithSession,
});
const consentsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/freigaben",
  component: ConsentsWithSession,
});
const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/einstellungen",
  component: SettingsWithSession,
});
const marketRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/markt",
  component: MarketWithSession,
});
const transfersRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/transfers",
  component: TransfersWithSession,
});
const companyTransfersRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/transfers",
  component: CompanyTransfersWithSession,
});
const companyJobsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/jobs",
  component: CompanyJobsWithSession,
});
const companyProfileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/profile",
  component: CompanyProfileWithSession,
});
const teamRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/team",
  component: TeamWithSession,
});
const invitationRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/invitation",
  component: InvitationRoute,
});
const companyNewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/company/new",
  component: CompanyNewWithSession,
});

const routeTree = rootRoute.addChildren([
  homeRoute,
  loginRoute,
  registerRoute,
  verifyRoute,
  profileRoute,
  portfolioRoute,
  resumeRoute,
  candidatesRoute,
  jobsRoute,
  careerRoute,
  applicationsRoute,
  consentsRoute,
  myDataRoute,
  settingsRoute,
  marketRoute,
  transfersRoute,
  companyTransfersRoute,
  companyJobsRoute,
  companyProfileRoute,
  teamRoute,
  invitationRoute,
  companyNewRoute,
]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
