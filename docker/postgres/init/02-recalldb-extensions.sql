-- Ensure the pgvector (and pg_trgm) extensions are installed in the RecallDB database on first init.
-- Requires the pgvector binaries to be present in the image (see postgres/Dockerfile). Idempotent via
-- IF NOT EXISTS, so re-runs are harmless.
\connect recalldb
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
