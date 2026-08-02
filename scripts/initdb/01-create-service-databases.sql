-- One database per service: ADR-0004 forbids a shared database and any
-- cross-service repository. `identity` is created by the postgres entrypoint
-- via POSTGRES_DB; every additional service database is created here.
--
-- Runs only on first container start (empty data volume). After adding a
-- database here, recreate the volume: `docker compose down -v && docker compose up -d`.
CREATE DATABASE consent OWNER worker;
CREATE DATABASE profile OWNER worker;
CREATE DATABASE resume OWNER worker;
