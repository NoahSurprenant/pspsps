# The client, built to static files.
FROM node:24-alpine AS client
WORKDIR /client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

# The server.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Pspsps.Server/Pspsps.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=client /client/dist ./wwwroot
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
# HTTP, then Orleans' silo-to-silo and gateway ports.
EXPOSE 8080 11111 30000
ENTRYPOINT ["dotnet", "Pspsps.Server.dll"]
