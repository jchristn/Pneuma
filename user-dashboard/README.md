# Pneuma User Dashboard

The fan-facing exploration experience for **Pneuma — Pneuma - information brought to life**. Users
search a subject's official knowledge graph, land on a node to see its contents,
links, adjacent nodes, and the relationships between them, and ask questions that
are answered from grounded, cited sources.

Built with **React 19 + Vite 6 + react-router-dom 7**. The API layer is a
hand-rolled `fetch` client (no axios). No charting library is used.

## Routes

| Route        | Purpose                                                                                          |
| ------------ | ------------------------------------------------------------------------------------------------ |
| `/login`     | Server URL + email/password sign-in, Pneuma branding, theme + language controls.                    |
| `/`          | **Search.** Prominent search box → `GET /v1.0/search`; representative nodes as clickable cards.   |
| `/node/:id`  | **Node explorer.** Node contents, links (open in a new tab), adjacent nodes, and relationships.   |
| `/ask`       | **Ask.** Question box → `POST /v1.0/query`; grounded answer + source cards.                       |

All routes except `/login` are protected; an unauthenticated visit redirects to login.
Any URL found in node content (or a URL-bearing tag) is rendered as an anchor with
`target="_blank" rel="noopener noreferrer"` — links always open in a new tab.

## API

Base URL is entered at login (default `http://localhost:8080`). Auth is
`POST /v1.0/token { email, password } -> { token }`, and the bearer token is sent on
every subsequent call. Endpoints used:

- `GET /v1.0/search?q=<query>&max=20`
- `GET /v1.0/graph/nodes/{id}` · `/neighbors` · `/edges`
- `POST /v1.0/query { question, maxResults }`
- `GET /v1.0/token` (token validation) · `DELETE /v1.0/token` (logout)

Default demo credentials: `admin@pneuma` / `password`.

## Development

```bash
npm install
npm run dev      # http://localhost:3012
npm run build    # production build to dist/
npm run preview  # serve the production build
```

## Project structure

```
src/
├── main.jsx                # entry: i18n + Theme + Auth + Router providers
├── App.jsx                 # routes + protected/public guards
├── index.css               # CSS-variable theming (light/dark)
├── context/                # AuthContext, ThemeContext
├── i18n/                   # i18next scaffold (index, localeRegistry, resources, formatters)
├── utils/                  # api.js (ApiClient), nodes.js (link/edge helpers)
├── components/             # Shell, Login, SearchBox, NodeCard, Modal, CopyButton,
│                           #   ThemeToggle, LanguageSelector, Icon
└── views/                  # SearchView, NodeView, AskView
```

## Theming & i18n

- Light/dark themes via CSS variables; the preference is persisted in `localStorage`
  and applied to `document.documentElement[data-theme]`. Respects `prefers-color-scheme`.
- i18n uses `i18next` + `react-i18next` + `i18next-browser-languagedetector` with an
  English catalog (`src/i18n/resources.js`). `<html lang/dir>` is kept in sync and the
  choice persists across refreshes. Locale-aware formatters live in `src/i18n/formatters.js`.

## Docker

```bash
docker build -t pneuma-user-dashboard .
docker run -p 8080:80 pneuma-user-dashboard
```

Multi-stage build (`node:20-alpine` → `nginx:alpine`). `nginx.conf` uses
`try_files … /index.html` for SPA client-side routing.
