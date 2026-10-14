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
COPY --from=backend-build /app/publish ./
COPY --from=frontend /src/web-client/dist ./wwwroot
RUN mkdir -p /data && chown -R app:app /app /data
ENV ASPNETCORE_URLS=http://+:8080
ENV DATABASE_PATH=/data/rustplus-web.db
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "RustPlusWeb.dll"]
