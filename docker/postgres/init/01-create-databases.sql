-- Create the per-service databases on the shared Pneuma Postgres instance on first init.
-- Idempotent via \gexec so re-runs are harmless. Each subordinate service (Less3, LiteGraph, Partio,
-- RecallDB) owns its own database; Pneuma itself uses the POSTGRES_DB ("pneuma") database. The RecallDB
-- database's vector/pg_trgm extensions are enabled by 02-recalldb-extensions.sql.
SELECT 'CREATE DATABASE less3'     WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'less3')\gexec
SELECT 'CREATE DATABASE litegraph' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'litegraph')\gexec
SELECT 'CREATE DATABASE partio'    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'partio')\gexec
SELECT 'CREATE DATABASE recalldb'  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'recalldb')\gexec
