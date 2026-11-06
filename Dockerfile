# Gridline: one image serves the API, the live hub and the built web app.
#   docker build -t gridline .
#   docker run -p 8080:8080 -e ConnectionStrings__Gridline="Host=db;Database=gridline;Username=gridline;Password=..." gridline
# See docs/DEPLOY.md. The image applies db/migrations on start-up (Db__AutoMigrate=true) and keeps uploads in /data.

# ---- 1. web app -------------------------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- 2. API ------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/api
COPY api/global.json api/Gridline.slnx ./
COPY api/src/Gridline.Api/Gridline.Api.csproj src/Gridline.Api/
RUN dotnet restore src/Gridline.Api/Gridline.Api.csproj
COPY api/ ./
RUN dotnet publish src/Gridline.Api/Gridline.Api.csproj -c Release -o /out --no-restore

# ---- 3. runtime --------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# QuestPDF (PDF exports) needs fontconfig and a sans-serif font on Linux
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core ca-certificates curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /src/web/dist ./web/dist
COPY db/migrations ./db/migrations
RUN mkdir -p /data && chown -R app:app /app /data
USER app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Web__Dist=/app/web/dist \
    Db__MigrationsPath=/app/db/migrations \
    Db__AutoMigrate=true \
    Storage__Root=/data \
    DOTNET_gcServer=0
VOLUME ["/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 CMD curl -fsS http://localhost:8080/api/health/live || exit 1
ENTRYPOINT ["dotnet", "Gridline.Api.dll"]
