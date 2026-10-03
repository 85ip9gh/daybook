FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Ledger.Domain/Ledger.Domain.csproj src/Ledger.Domain/
COPY src/Ledger.Api/Ledger.Api.csproj src/Ledger.Api/
RUN dotnet restore src/Ledger.Api/Ledger.Api.csproj
COPY src/Ledger.Domain/ src/Ledger.Domain/
COPY src/Ledger.Api/ src/Ledger.Api/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]
