# syntax=docker/dockerfile:1
#
# design.md §10 (slice 7.6). Three stages: build the web SPA, publish the API, assemble the
# smallest runtime image. Tests do NOT run here — CI's test-api/test-web jobs run them separately,
# before this image is ever built, so a broken test cannot ship inside a green-looking image build.

# ---- Stage 1: the SPA -------------------------------------------------------------------------
FROM node:22-alpine AS web-build
WORKDIR /web
# Dependency layer cached separately from source so an app-code-only change doesn't reinstall
# node_modules.
COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci
COPY src/Web/ ./
RUN npm run build
# Output: /web/dist (index.html, sw.js, manifest.webmanifest, hashed assets — vite's public/
# passthrough already lands sw.js/manifest.webmanifest at the dist root, which web push needs).

# ---- Stage 2: the API --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
# Restore first, from the project file alone, so a source-only change doesn't re-resolve NuGet.
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app/publish --no-restore

# ---- Stage 3: the runtime image ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api-build /app/publish .
# The SPA lands in wwwroot — Program.cs checks for wwwroot/index.html at boot and only then turns
# on UseStaticFiles/UseDefaultFiles and the SPA fallback route (design.md §10, slice 7.6). A
# container without this stage (or with the copy failing) boots as an API-only server, same as
# dotnet run from source — never a silently-broken SPA.
COPY --from=web-build /web/dist ./wwwroot

# design.md §10: production never auto-migrates on startup, and neither does test — the CI
# deploy-test job runs `dotnet ef migrations bundle` before updating the container, not this image.
ENTRYPOINT ["dotnet", "Api.dll"]
