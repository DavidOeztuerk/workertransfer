# ADR-0015: Geteilte JWT-Middleware in `worker-auth`; ein Trust-Domain-Secret

Date: 2026-07-31
Status: Accepted
Related: ADR-0002 (Kernel vs. Bausteine), ADR-0004 §1 (kein Cross-Service-Domain-Modell), ADR-0007 (HS256/PyJWT), ADR-0009 (Compose-Hook, rohes ASGI), ADR-0014 (Duplikate driften)

## Kontext

`consent-service` muss den Tokens von `identity-service` vertrauen. Die Design-Spec
für Sub-step 3.1 sagt *dass*, aber nicht *wie*. Der naheliegende Weg wäre,
`apps/identity-service/.../presentation/auth_middleware.py` zu kopieren. Das ist
aus zwei unabhängigen Gründen falsch:

1. `JwTokenService` und `AuthPrincipal` sind **service-privat**. Ein
   Cross-Service-Import verletzt ADR-0002/0004 — es gibt kein geteiltes
   Domain-Modell.
2. Eine Kopie hätte einen gerade erst behobenen Bug reproduziert. Bis Phase 2.5 las
   diese Middleware das Token **nur** aus `Authorization: Bearer`, während der Login
   es als `httpOnly`-Cookie ausliefert — `GET /me` war aus jedem Browser
   unerreichbar. Zwei Kopien driften genau so auseinander, wie ADR-0014 es für
   `worker-exceptions` dokumentiert (gleicher Funktionsname, abweichendes Verhalten).

## Entscheidung

**`worker_auth.JwtAuthMiddleware` ist der geteilte Baustein.** Nicht der Kernel:
Authentifizierung ist opt-in pro Service, und ADR-0002 hält den Kernel klein und
immer-an, während `worker-*`-Pakete die komponierbaren, opt-in Bausteine sind.
Services installieren sie über den Compose-Hook aus ADR-0009
(`create_api_app(..., auth_middleware=..., auth_middleware_kwargs=...)`).

Eigenschaften, die damit **einmal** existieren statt pro Service:

- **Header vor Cookie.** Ein explizit mitgegebenes Credential schlägt ein
  ambientes aus dem Cookie-Jar — sonst könnte ein veraltetes Session-Cookie den
  Aufrufer stillschweigend überstimmen.
- **Rohes ASGI**, kein `BaseHTTPMiddleware`: das führt die Downstream-App in einem
  eigenen Task aus und bricht die ContextVar-Propagation (Grund, aus dem ADR-0009
  `worker-middleware` gelöscht hat).
- **Fehlschlag ⇒ `None`, kein 401.** Öffentliche Endpunkte funktionieren weiter;
  jede Route entscheidet selbst.

`verify` ist ein **einfaches Callable**. Damit lernt `worker-auth` nie den
Principal-Typ eines Service kennen (ADR-0004-Grenze): `identity-service` reicht
seinen `JwTokenService.verify_access_token` hinein, `consent-service` bindet
`TokenManager.verify_token(..., expected_type="access")`. Eine neue
Verifikationsfunktion war nicht nötig — `TokenManager` liefert bereits einen
transportneutralen `TokenPayload`.

**Geteiltes HS256-Secret = eine Trust-Domain.** `identity-service` signiert,
`consent-service` verifiziert nur; beide lesen dasselbe `WORKER_JWT_SECRET`
(ADR-0007). Das ist eine bewusste Annahme, keine Nachlässigkeit: solange alle
Services in einer Vertrauensdomäne laufen, ist ein symmetrisches Secret angemessen.
`.env.example` und `docker-compose.yml` machen es explizit (ein `WORKER_JWT_SECRET`
für alle Service-Container).

## Konsequenzen

- Ein Service, der Tokens verifiziert, schreibt keine Middleware mehr — er bindet
  eine Verifikationsfunktion. Service Nr. 4 bis 21 erben Header-**und**-Cookie
  automatisch.
- **Bekannte Grenze:** ein geteiltes symmetrisches Secret bedeutet, dass *jeder*
  Service, der Tokens verifizieren kann, auch welche **ausstellen** könnte. Das ist
  in einer Trust-Domain tragbar, aber nicht dauerhaft. Der Ausstieg ist asymmetrisch
  (RS256/EdDSA): identity-service hält den privaten Schlüssel, alle anderen nur den
  öffentlichen. `TokenManager` lehnt heute alles außer HS256 ausdrücklich ab — die
  Umstellung ist damit eine sichtbare, nicht schleichende Änderung. Fällig mit dem
  Gateway in Phase 10.
- Ein Secret-Rotationslauf muss alle Services gleichzeitig erfassen.

## Verifikation

- `packages/worker-auth/tests/test_jwt_middleware.py` — beide Carrier,
  Header-Präzedenz, konfigurierbarer Cookie-/State-Name, defekte Eingaben,
  erhaltener Scope-State, Nicht-HTTP-Scopes.
- `apps/identity-service/tests/unit/test_auth_middleware.py` — **unverändert** grün
  nach der Umstellung; das ist der Beweis für identisches Verhalten.
- `apps/consent-service/tests/integration/test_consent_endpoints.py` — mintet ein
  Token mit dem geteilten Secret und wird vom Ledger akzeptiert.
