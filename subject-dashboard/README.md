# Pneuma Subject Dashboard

A React 19 + Vite 6 single-page application for subjects/subjects to manage their
content and knowledge graph on the Pneuma (Pneuma - information brought to life) platform. It
lets an subject supply links to their content, watch ingestion progress, and
diagnose failures.

## Stack

- React 19, Vite 6, react-router-dom 7
- Hand-rolled `fetch` API client (`src/utils/api.js`) — no axios
- Hand-rolled SVG activity chart (`src/components/ActivityChart.jsx`) — no chart library
- i18next + react-i18next (English + partial Spanish), locale-aware formatters
- Light/dark theme via CSS variables (persisted in localStorage)

## Getting started

```bash
npm install
npm run dev      # dev server on http://localhost:3011
npm run build    # production build to dist/
npm run preview  # preview the production build
```

At the login screen enter the Pneuma server URL (default `http://localhost:8080`),
your email, and password. The dashboard authenticates with
`POST /v1.0/token`, sends the bearer token on all calls, validates with
`GET /v1.0/token`, and revokes it on logout with `DELETE /v1.0/token`.

## Routes

| Route | Description |
| --- | --- |
| `/` | Login (server URL + email + password) |
| `/dashboard/home` | Overview: KPI tiles + request activity chart |
| `/dashboard/subjects` | My Subjects — list + create/edit modal |
| `/dashboard/links` | Content Links (core) — submit links, track status, last ingested, last error |
| `/dashboard/ingestion` | Ingestion jobs — list, filter, stage-timeline detail, restart failed |
| `/dashboard/requests` | Request History — KPIs, chart, filters, inspector modal |
| `/dashboard/explorer` | OpenAPI-driven API Explorer |
| `/dashboard/settings` | Server info + auth context |

## API surface consumed

Subjects (`/v1.0/subjects`), content links (`/v1.0/links`,
`/v1.0/subjects/{id}/links`), ingestion jobs (`/v1.0/jobs`, `/v1.0/jobs/{id}`,
`/v1.0/jobs/{id}/restart`), request history (`/v1.0/api/request-history*`),
health (`/v1.0/api/health`), and OpenAPI (`/openapi.json`).

Response shapes are handled defensively (`asArray` in `src/utils/api.js`)
because the exact list envelope was not available at build time.

## Docker

```bash
docker build -t pneuma-subject-dashboard .
docker run -p 3011:80 pneuma-subject-dashboard
```

The multi-stage build compiles with `node:20-alpine` and serves the static
bundle from `nginx:alpine` with SPA `try_files` fallback (`nginx.conf`).
