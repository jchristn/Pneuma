# Pneuma Admin Dashboard

Operator console for **Pneuma — Pneuma - information brought to life**. Full read/write administration of tenants,
users, RBAC, content ingestion, model runners, prompts, plus observability (request history,
API explorer) and server settings.

Built with **React 19 + Vite 6 + react-router-dom 7**. Hand-rolled `fetch` API client (no axios)
and hand-rolled SVG charts (no charting library). i18n via i18next / react-i18next.

## Getting started

```bash
npm install
npm run dev      # http://localhost:3010
npm run build    # production build to dist/
npm run preview
```

On the login screen enter the server URL (default `http://localhost:8080`) and credentials.
Local default admin: `admin@pneuma` / `password`.

## Architecture

```
src/
  main.jsx / App.jsx        entry + routing (protected routes, persisted session)
  context/AuthContext.jsx   session (login/logout), token, theme, auth context
  utils/api.js              hand-rolled fetch ApiClient + normalizeList
  utils/openApi.js          OpenAPI flatten / body templates / code snippets
  hooks/useApiExplorer.js   OpenAPI-driven explorer state + execution
  i18n/                     i18next bootstrap, locale registry, formatters, LanguageSelector
  components/               shell (Sidebar/Topbar/Dashboard), DataTable, Modal, ConfirmModal,
                            JsonViewer, ActionMenu, ActivityChart, RequestDetailsModal, etc.
  views/                    one per route section
  config/nav.js             grouped navigation model + section metadata
```

## Routes

All authenticated routes live under `/dashboard/:section`:

| Group | Sections |
|-------|----------|
| Overview | `home` |
| Content | `subjects`, `links`, `jobs` (Ingestion Queue) |
| Administration | `tenants`, `users`, `credentials`, `roles`, `permissions`, `assignments`, `audit` |
| Configuration | `model-runners`, `prompts` |
| Observability | `requests` (Request History), `explorer` (API Explorer) |
| System | `settings` |

## Theme & i18n

- Light/dark theme via CSS variables, persisted in `localStorage`, toggled from the topbar.
- English catalog ships in `src/i18n/resources.js`; add locales in `localeRegistry.js`.

## Docker

```bash
docker build -t pneuma-admin-dashboard .
docker run -p 3010:80 pneuma-admin-dashboard
```

Multi-stage build (`node:20-alpine` → `nginx:alpine`) with SPA `try_files` in `nginx.conf`.

## Responsive

Verified layout targets at 1280px (desktop), 768px (tablet), and 390px (mobile). The sidebar
collapses behind a hamburger below 900px; tables scroll horizontally within their frame.
