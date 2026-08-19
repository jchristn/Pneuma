# Pneuma — Ports & Default Credentials

Everything is published on `localhost` by `docker compose up`. **Change these defaults before exposing any service.**

## Dashboards

| Dashboard | URL | Login |
|-----------|-----|-------|
| Pneuma Admin | http://localhost:3010 | `admin@pneuma` / `password` |
| Pneuma Subject | http://localhost:3011 | `admin@pneuma` / `password` |
| Pneuma User | http://localhost:3012 | `admin@pneuma` / `password` |
| Grafana | http://localhost:3000 | `admin` / `admin` |
| LiteGraph UI | http://localhost:3001 | token `litegraphadmin` |
| DocumentAtom UI | http://localhost:3002 | none |
| Partio dashboard | http://localhost:8401 | token `partioadmin` |
| RecallDB dashboard | http://localhost:8601 | token `recalldbadmin` |

## Services

| Service | Port | Auth |
|---------|------|------|
| Pneuma API | 8080 | `admin@pneuma` / `password`; admin key `pneumaadmin` |
| LiteGraph | 8701 | `litegraphadmin` |
| DocumentAtom | 8000 | none |
| Partio | 8400 | `partioadmin` |
| RecallDB | 8600 | `recalldbadmin` |
| Ollama | 11434 | none |
| Prometheus | 9090 | none |
| Tempo | 3200 (OTLP 4317/4318) | none |
| Postgres | 15432 | `pneuma` / `pneuma` |
