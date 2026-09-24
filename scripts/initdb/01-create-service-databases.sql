-- One database per service: ADR-0004 forbids a shared one and any
-- cross-service store. `identity` is created by the Postgres entrypoint via
-- POSTGRES_DB; every further one stands here.
--
-- Runs ONLY at the container's first start (empty volume). After an addition
-- here, recreate the volume:
--   docker compose down -v && docker compose up -d
--
-- What is missing is a database for the gateway: it holds nothing.
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
CREATE DATABASE assessment OWNER worker;
