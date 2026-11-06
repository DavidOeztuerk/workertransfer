#!/usr/bin/env bash
# One role per service, owner of its own database and of nothing else (ADR-0058).
# ADR-0004 forbids a shared database; the gateway holds nothing and has none.
#
# Three callers: the image's entrypoint on an empty volume, `make db-roles` on an
# existing one, and the same file out of the ConfigMap in the cluster. It may run
# any number of times, and that is how a new service gets its role and database.
#
# The entrypoint runs this file only when it is executable and SOURCES it
# otherwise, which would make an `exit` end the setup. So the mode is 755 here
# and 0555 in the ConfigMap, and no such line stands below. `make db-roles` runs
# it as root without PGUSER, hence the explicit `--username`.
set -euo pipefail

# docker-compose.yml starts the same list; DatabaseRoleTests compares the two.
# This file is mounted alone, so it cannot read a list from beside it.
databases="identity consent profile resume portfolio jobs applications companies transfer notification github scout advisor assessment messages"

superuser="${POSTGRES_USER:-worker}"

# With the default, a statement that fails stands in the server log with its
# text, and for an ALTER ROLE that text is the password.
export PGOPTIONS="-c log_min_error_statement=panic"

as_superuser() {
  psql --no-psqlrc --quiet --set ON_ERROR_STOP=1 --username "$superuser" "$@"
}

password_variable() {
  printf 'WORKERTRANSFER_DB_PASSWORD_%s' "$(printf %s "$1" | tr '[:lower:]' '[:upper:]')"
}

missing=0
for database in $databases; do
  variable=$(password_variable "$database")
  if [ -z "${!variable:-}" ]; then
    echo "$variable is not set." >&2
    missing=1
  fi
done
[ "$missing" -eq 0 ]

# Everything out of `postgres`: a role owns its database with all that lies in
# it, a `search_path` and a `public.now()` included, and a plain `SELECT now()`
# from the superuser in there would make that role a superuser. Measured.
for database in $databases; do
  # The password arrives by \getenv and not as an argument, which `ps` would show.
  as_superuser --dbname postgres \
    --set role="svc_$database" \
    --set database="$database" \
    --set variable="$(password_variable "$database")" <<'SQL'
\getenv password :variable
SELECT format('CREATE ROLE %I', :'role')
 WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'role') \gexec
ALTER ROLE :"role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS
  PASSWORD :'password';
SELECT format('CREATE DATABASE %I OWNER %I', :'database', :'role')
 WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'database') \gexec
REVOKE CONNECT, TEMPORARY ON DATABASE :"database" FROM PUBLIC;
SQL
done

# CONNECT stands open to PUBLIC by default, here as anywhere. And the superuser
# ends its network login in every run, whatever an old volume carried: POSTGRES_PASSWORD
# works only on an empty one. The socket and 127.0.0.1 stay trusted, inside this
# container alone.
as_superuser --dbname postgres --set superuser="$superuser" <<'SQL'
REVOKE CONNECT, TEMPORARY ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT, TEMPORARY ON DATABASE template1 FROM PUBLIC;
ALTER ROLE :"superuser" PASSWORD NULL;
SQL

# A database that still belongs to the superuser changes hands object by object,
# because REASSIGN OWNED refuses the bootstrap superuser. One transaction each:
# "belongs to the role" then means that all of it went through, which is what
# makes the next run skip it and the ring be emptied only once.
for database in $databases; do
  owner=$(as_superuser --dbname postgres --tuples-only --no-align \
    --command "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$database'")

  if [ "$owner" != "$superuser" ]; then
    continue
  fi

  as_superuser --dbname "$database" --set role="svc_$database" --set database="$database" <<'SQL'
BEGIN;

-- Tables first: indexes and their linked sequences go along, and ALTER SEQUENCE
-- on those would refuse. 16384 is where the system's own objects end.
SELECT format('ALTER TABLE %s OWNER TO %I', c.oid::regclass, :'role')
  FROM pg_class c
 WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
   AND c.oid >= 16384
   AND pg_get_userbyid(c.relowner) = current_user
   AND NOT EXISTS (SELECT FROM pg_depend d
                    WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e') \gexec

SELECT format('ALTER SEQUENCE %s OWNER TO %I', c.oid::regclass, :'role')
  FROM pg_class c
 WHERE c.relkind = 'S'
   AND c.oid >= 16384
   AND pg_get_userbyid(c.relowner) = current_user
   AND NOT EXISTS (SELECT FROM pg_depend d
                    WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e') \gexec

-- 'i' leaves out array and row types, which refuse and follow their parent. 'e'
-- matters more: on a type of an extension the statement succeeds silently.
SELECT format('ALTER TYPE %s OWNER TO %I', t.oid::regtype, :'role')
  FROM pg_type t
 WHERE t.oid >= 16384
   AND pg_get_userbyid(t.typowner) = current_user
   AND NOT EXISTS (SELECT FROM pg_depend d
                    WHERE d.classid = 'pg_type'::regclass AND d.objid = t.oid AND d.deptype IN ('e', 'i')) \gexec

SELECT format('ALTER ROUTINE %s OWNER TO %I', p.oid::regprocedure, :'role')
  FROM pg_proc p
 WHERE p.oid >= 16384
   AND pg_get_userbyid(p.proowner) = current_user
   AND NOT EXISTS (SELECT FROM pg_depend d
                    WHERE d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e') \gexec

-- Under the old master key its entries no longer open and would fail at every
-- start (ADR-0058). The services write new ones by themselves; on an empty
-- volume the table does not exist yet.
SELECT 'TRUNCATE public.dataprotection_keys'
 WHERE to_regclass('public.dataprotection_keys') IS NOT NULL \gexec

-- Whatever carries an owner and was not moved above stops the handover. The
-- catalogs are read and not listed, so that a kind of object nobody thought of
-- falls here instead of being left behind. Extensions stay with the superuser:
-- `citext` is usable by all, and its members are excepted.
DO $check$
DECLARE
  catalog record;
  left_behind text;
BEGIN
  FOR catalog IN
    SELECT c.oid AS id, c.oid::regclass AS relation, a.attname AS owner_column
      FROM pg_class c
      JOIN pg_attribute a ON a.attrelid = c.oid
     WHERE c.relnamespace = 'pg_catalog'::regnamespace
       AND c.relkind = 'r'
       AND NOT c.relisshared
       AND c.oid <> 'pg_extension'::regclass
       AND a.attname LIKE '%owner'
       AND a.atttypid = 'oid'::regtype
  LOOP
    EXECUTE format(
      $query$
      SELECT (pg_identify_object(%1$s, o.oid, 0)).identity
        FROM %2$s o
       WHERE pg_get_userbyid(o.%3$I) = current_user
         AND o.oid >= 16384
         AND o.oid NOT IN (SELECT oid FROM pg_namespace WHERE nspname LIKE 'pg\_%%')
         AND NOT EXISTS (SELECT FROM pg_depend d
                          WHERE d.classid = %1$s AND d.objid = o.oid AND d.deptype IN ('e', 'i'))
       LIMIT 1
      $query$,
      catalog.id, catalog.relation, catalog.owner_column)
      INTO left_behind;

    IF left_behind IS NOT NULL THEN
      RAISE EXCEPTION '% still belongs to the superuser', left_behind;
    END IF;
  END LOOP;
END
$check$;

ALTER DATABASE :"database" OWNER TO :"role";

COMMIT;
SQL
done
