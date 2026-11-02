# Host app (shelf, movie night links, cleanup, admin page).
# Build from the repo root:  docker build -f src/Tlp.Host/Dockerfile -t the-last-piece/host .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Tlp.Core/Tlp.Core.csproj src/Tlp.Core/
COPY src/Tlp.Host/Tlp.Host.csproj src/Tlp.Host/
RUN dotnet restore src/Tlp.Host/Tlp.Host.csproj
COPY src/Tlp.Core/ src/Tlp.Core/
COPY src/Tlp.Host/ src/Tlp.Host/
RUN dotnet publish src/Tlp.Host/Tlp.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /shelf /data && chown -R app:app /shelf /data
USER app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Tlp__ShelfDir=/shelf \
    Tlp__DataDir=/data
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tlp.Host.dll"]
