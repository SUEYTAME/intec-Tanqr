# Imagen de producción de un solo origen: Kestrel (TLS 1.3) sirve la API y la interfaz compilada.
# Construir desde la raíz del repositorio:  docker build -t intec-combustible:<versión> .

FROM node:24.15.0-bookworm-slim AS web
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/backend
# .editorconfig de la raíz: marca las migraciones como código generado para los analizadores.
COPY .editorconfig /src/.editorconfig
COPY backend/ ./
RUN dotnet publish src/Combustible.Api/Combustible.Api.csproj -c Release -o /out --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Fuente para los PDF (SystemFontResolver busca DejaVu en Linux) y GSSAPI, que Npgsql sondea al conectar.
RUN apt-get update \
 && apt-get install -y --no-install-recommends fonts-dejavu-core libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /src/frontend/dist ./wwwroot
ENV WEB_ROOT=/app/wwwroot \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=https://+:8443 \
    OUTBOX_DIR=/app/outbox
RUN mkdir -p /app/outbox && chown app:app /app/outbox
USER app
EXPOSE 8443
ENTRYPOINT ["dotnet", "Combustible.Api.dll"]
