# Standard PostgreSQL 17 with the pgvector extension installed, so RecallDB's `recalldb` database can
# enable `vector` (and pg_trgm). The base image already carries the PGDG apt repository that publishes the
# per-version pgvector package. The extensions themselves are enabled per-database by the init scripts.
FROM postgres:17

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-17-pgvector \
    && rm -rf /var/lib/apt/lists/*
