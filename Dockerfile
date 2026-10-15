# Build the browser app.
FROM node:22-alpine AS frontend
WORKDIR /src/web-client
COPY web-client/package*.json ./
RUN npm install
COPY web-client/ ./
RUN npm run build

# Build the ASP.NET Core service.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY Web/RustPlusWeb.csproj Web/
RUN dotnet restore Web/RustPlusWeb.csproj
COPY Web/ Web/
RUN dotnet publish Web/RustPlusWeb.csproj -c Release -o /app/publish --no-restore

# Runtime image has no SDK or frontend toolchain.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl sqlite3 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/keys \
    && chown -R app:app /app /data
COPY --from=backend-build --chown=app:app /app/publish ./
COPY --from=frontend --chown=app:app /src/web-client/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
ENV DATABASE_PATH=/data/rustplus-web.db
ENV DATA_PROTECTION_KEYS_PATH=/data/keys
ENV FCM_CREDENTIALS_PATH=/data/fcm-credentials.protected
ENV FCM_PERSISTENT_IDS_PATH=/data/fcm-persistent-ids.protected
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD curl --fail --silent http://127.0.0.1:8080/api/health/ready || exit 1
USER app
ENTRYPOINT ["dotnet", "RustPlusWeb.dll"]
