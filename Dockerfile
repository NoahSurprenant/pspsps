# The client, built to static files.
FROM node:24-alpine AS client
WORKDIR /client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
# The commit, for service.version on the browser's telemetry (passed by the publish workflow; empty locally).
ARG SOURCE_REVISION=""
RUN SOURCE_REVISION=$SOURCE_REVISION npm run build

# The server.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY analyzers/ analyzers/
COPY src/ src/
# The commit, for service.version in telemetry (passed by the publish workflow; empty locally).
ARG SOURCE_REVISION=""
# The analyzers (Directory.Build.props) run in CI's .NET job; the image build skips them.
RUN dotnet publish src/Pspsps.Server/Pspsps.Server.csproj -c Release -o /app -p:SourceRevisionId=$SOURCE_REVISION -p:RunAnalyzers=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=client /client/dist ./wwwroot
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
# HTTP, then Orleans' silo-to-silo and gateway ports.
EXPOSE 8080 11111 30000
ENTRYPOINT ["dotnet", "Pspsps.Server.dll"]
