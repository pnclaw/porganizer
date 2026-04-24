# ─── Stage 1: Build Vue frontend ────────────────────────────────────────────
FROM node:22-alpine AS frontend-build

WORKDIR /frontend

COPY src/porganizer.Frontend/package*.json ./
RUN npm ci

COPY src/porganizer.Frontend/ ./
RUN npm run build

# ─── Stage 2: Build .NET backend ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build

WORKDIR /app

COPY src/ ./src/
RUN dotnet publish ./src/porganizer.Api/porganizer.Api.csproj -c Release -o /app/publish

# ─── Stage 3: Download static ffmpeg binary ──────────────────────────────────
# Runs on the build machine's native architecture (--platform=$BUILDPLATFORM)
# so apt-get and the download are never subject to QEMU emulation, even when
# cross-compiling for arm64. TARGETARCH is injected by BuildKit and matches
# the johnvansickle.com filename convention (amd64 / arm64).
FROM --platform=$BUILDPLATFORM debian:bookworm-slim AS ffmpeg-downloader

ARG TARGETARCH

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && wget -qO /tmp/ffmpeg.tar.xz \
         "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-${TARGETARCH}-static.tar.xz" \
    && tar -xJf /tmp/ffmpeg.tar.xz -C /tmp \
    && mv /tmp/ffmpeg-*-static/ffmpeg /usr/local/bin/ffmpeg \
    && mv /tmp/ffmpeg-*-static/ffprobe /usr/local/bin/ffprobe \
    && chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe

# ─── Stage 4: Runtime image ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# Copy the static ffmpeg/ffprobe binaries — no apt-get required in the runtime image
COPY --from=ffmpeg-downloader /usr/local/bin/ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg-downloader /usr/local/bin/ffprobe /usr/local/bin/ffprobe

# Copy published .NET app
COPY --from=backend-build /app/publish ./

# Copy Vue SPA into wwwroot so ASP.NET Core serves it statically
COPY --from=frontend-build /frontend/dist ./wwwroot

# Create mount points for persistent data and logs
RUN mkdir -p /app/data /app/logs

# SQLite database path (overridable via environment variable or volume)
ENV DB_PATH=/app/data/app.db

# Bind on all interfaces, port 8080
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "porganizer.Api.dll"]
