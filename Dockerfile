FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json .
COPY src/Haven.Web/Haven.Web.csproj src/Haven.Web/
RUN dotnet restore src/Haven.Web/Haven.Web.csproj
COPY src/Haven.Web/ src/Haven.Web/
RUN dotnet publish src/Haven.Web/Haven.Web.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Haven.Web.dll"]
