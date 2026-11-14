# One production image: the API plus the built web front end (served from wwwroot).
# Build from the repo root:  docker build -f deploy/Dockerfile -t porch-light .

# --- Web front end ---
FROM node:20-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# --- API ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY global.json ./
COPY api/PorchLight.Api/PorchLight.Api.csproj api/PorchLight.Api/
RUN dotnet restore api/PorchLight.Api/PorchLight.Api.csproj
COPY api/PorchLight.Api/ api/PorchLight.Api/
RUN dotnet publish api/PorchLight.Api/PorchLight.Api.csproj -c Release -o /app --no-restore

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api /app .
COPY --from=web /web/dist ./wwwroot
# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PorchLight.Api.dll"]
