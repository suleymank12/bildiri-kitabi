# syntax=docker/dockerfile:1

# Build: restore first from the project files only, so the package layer is reused until they change.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BildiriKitabi.Core/BildiriKitabi.Core.csproj src/BildiriKitabi.Core/
COPY src/BildiriKitabi.Infrastructure/BildiriKitabi.Infrastructure.csproj src/BildiriKitabi.Infrastructure/
COPY src/BildiriKitabi.Api/BildiriKitabi.Api.csproj src/BildiriKitabi.Api/
RUN dotnet restore src/BildiriKitabi.Api/BildiriKitabi.Api.csproj
COPY src/ src/
RUN dotnet publish src/BildiriKitabi.Api/BildiriKitabi.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# The storage directory is created here because the runtime image has no shell to run mkdir.
RUN mkdir -p /out/data/storage

# Runtime: Ubuntu 24.04 "chiseled-extra" ASP.NET image. No shell and no package manager, non-root user `app`
# (uid 1654); the "extra" variant ships ICU (tr-TR dates and casing) and tzdata. QuestPDF's native renderer
# was verified to produce the book PDF in this image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /out/data /data
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 Storage__RootPath=/data/storage
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "BildiriKitabi.Api.dll"]
