FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Raven.Api/Raven.Api.csproj src/Raven.Api/
RUN dotnet restore src/Raven.Api/Raven.Api.csproj
COPY src/Raven.Api/ src/Raven.Api/
RUN dotnet publish src/Raven.Api/Raven.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish/ .
RUN mkdir -p /app/data && chown -R "$APP_UID" /app/data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__Raven="Data Source=/app/data/raven.db"
ENV RAVEN_DATA_PROTECTION_PATH=/app/data/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Raven.Api.dll"]
