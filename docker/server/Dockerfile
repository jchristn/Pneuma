# Pneuma Server image. Build context is the repository root.
# Build: docker build -f docker/server/Dockerfile -t pneuma-server .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore and publish only the server (and its project references).
COPY src/ ./src/
RUN dotnet publish src/Pneuma.Server/Pneuma.Server.csproj -c Release -f net10.0 -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# curl for the compose healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Install the headless Chromium build (and its OS dependencies) that the ingestion crawler uses
# to render JavaScript-heavy pages via Playwright. Browsers are placed under a stable path shared
# by this build step and the running server.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet Pneuma.Server.dll install-browsers && rm -rf /var/lib/apt/lists/*

# Settings and runtime data are mounted from the docker/ directory at run time.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pneuma.Server.dll", "--config", "/app/config/pneuma.json"]
