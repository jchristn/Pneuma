-- Create the per-service databases on the shared Pneuma Postgres instance on first init.
-- Idempotent via \gexec so re-runs are harmless. Each subordinate service (Less3, LiteGraph,
-- Partio, Verbex) owns its own database; Pneuma itself uses the POSTGRES_DB ("pneuma") database.
SELECT 'CREATE DATABASE less3'     WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'less3')\gexec
SELECT 'CREATE DATABASE litegraph' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'litegraph')\gexec
SELECT 'CREATE DATABASE partio'    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'partio')\gexec
SELECT 'CREATE DATABASE verbex'    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'verbex')\gexec
