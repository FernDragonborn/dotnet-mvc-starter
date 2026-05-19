# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
ARG PROJ_PATH="./dotnet-mvc-starter.csproj"
WORKDIR /src

COPY "dotnet-mvc-starter.csproj" .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore $PROJ_PATH --runtime linux-musl-x64

COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build $PROJ_PATH -c $BUILD_CONFIGURATION -o /app/build --no-restore

# ── Publish stage ──
FROM build AS publish
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish $PROJ_PATH \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --runtime linux-musl-x64 \
    --self-contained false \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false

# ── Runtime stage (hardened) ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

# Security: non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

# Security: minimal attack surface
RUN apk --no-cache add --upgrade su-exec && \
    rm -rf /var/cache/apk/*

WORKDIR /app
COPY --from=publish /app/publish .
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh && \
    chown -R appuser:appgroup /app

# Performance: server GC, diagnostics for dotnet-counters
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=1
ENV DOTNET_gcServer=1

EXPOSE 8080

ENTRYPOINT ["/bin/sh", "/entrypoint.sh"]
