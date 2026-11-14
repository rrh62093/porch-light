# Build:  docker build -t porch-light-api -f api/Dockerfile api
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY PorchLight.Api/PorchLight.Api.csproj PorchLight.Api/
RUN dotnet restore PorchLight.Api/PorchLight.Api.csproj
COPY PorchLight.Api/ PorchLight.Api/
RUN dotnet publish PorchLight.Api/PorchLight.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PorchLight.Api.dll"]
