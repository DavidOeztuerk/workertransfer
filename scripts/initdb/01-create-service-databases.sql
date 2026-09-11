-- Eine Datenbank je Dienst: ADR-0004 verbietet eine gemeinsame und jeden
-- dienstuebergreifenden Speicher. `identity` legt der Postgres-Einstiegspunkt
-- ueber POSTGRES_DB an; jede weitere steht hier.
--
-- Laeuft NUR beim ersten Start des Behaelters (leerer Datentraeger). Nach einer
-- Ergaenzung hier den Datentraeger neu anlegen:
--   docker compose down -v && docker compose up -d
--
-- Was fehlt, ist eine Datenbank fuer das Gateway: es haelt nichts.
CREATE DATABASE consent OWNER worker;
CREATE DATABASE profile OWNER worker;
CREATE DATABASE resume OWNER worker;
CREATE DATABASE portfolio OWNER worker;
CREATE DATABASE jobs OWNER worker;
CREATE DATABASE applications OWNER worker;
CREATE DATABASE companies OWNER worker;
CREATE DATABASE transfer OWNER worker;
CREATE DATABASE notification OWNER worker;
CREATE DATABASE github OWNER worker;
CREATE DATABASE scout OWNER worker;
CREATE DATABASE advisor OWNER worker;
