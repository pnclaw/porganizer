# porganizer

A self-hosted private media manager built with .NET 10 and Vue 3.

## Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, EF Core 10, SQLite |
| Frontend | Vue 3, TypeScript, Vuetify 3, Vite |
| Logging | Serilog (console + rolling file) |
| Container | Docker, Docker Compose |

## Running the App

### Docker Hub (recommended)

```bash
docker run -d \
  -p 8080:8080 \
  -v porganizer-data:/app/data \
  -v porganizer-logs:/app/logs \
  --restart unless-stopped \
  pnclaw/porganizer:latest
```

App runs at [http://localhost:8080](http://localhost:8080). Data is persisted in the `porganizer-data` volume.

To use a specific version instead of `latest`:

```bash
docker run -d ... pnclaw/porganizer:1.0.0
```

### Docker Compose

Create a `docker-compose.yml` and run `docker compose up -d`:

```yaml
services:
  porganizer:
    image: pnclaw/porganizer:latest
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
      - ./logs:/app/logs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
```

App runs at [http://localhost:8080](http://localhost:8080). Data is persisted to `./data/app.db` and logs to `./logs/`.

## Development

### Hot reload (recommended)

Run the backend and frontend separately for a faster feedback loop.

**Backend**

```bash
dotnet run --project src/porganizer.Api
# http://localhost:5000
```

**Frontend**

```bash
cd src/porganizer.Frontend
npm install
npm run dev
# http://localhost:5173
```

The frontend dev server proxies `/api` requests to the backend automatically.

### Docker (build from source)

To test the production Docker build locally:

```bash
docker compose up -d --build
```

This builds the image from source and runs it at [http://localhost:8080](http://localhost:8080).

## Data Management

The SQLite database is stored at `/app/data/app.db` inside the container. Schema migrations run automatically every time the container starts — no manual steps needed when upgrading.

### Named volumes vs bind mounts

The `docker run` example uses **named volumes** (`porganizer-data`). Data lives inside Docker's managed storage — portable, but requires Docker commands to access directly.

The `docker-compose.yml` uses **bind mounts** (`./data`). The database file sits on your host filesystem at `./data/app.db`, making it easy to inspect, back up, or restore with ordinary file operations.

### Backup

**Named volume:**
```bash
docker cp $(docker ps -qf name=porganizer):/app/data/app.db ./backup.db
```

**Bind mount (Compose):**
```bash
cp ./data/app.db ./backup-$(date +%Y%m%d).db
```

### Restore

1. Stop the container:
   ```bash
   docker compose down        # or: docker stop <container>
   ```
2. Replace the database file:
   - Named volume: `docker cp ./backup.db $(docker ps -aqf name=porganizer):/app/data/app.db`
   - Bind mount: `cp ./backup.db ./data/app.db`
3. Start the container — any new migrations will apply automatically on top of the restored data.

### Start fresh

**Named volume:**
```bash
docker compose down
docker volume rm porganizer_porganizer-data
docker compose up -d
```

**Bind mount (Compose):**
```bash
docker compose down
rm ./data/app.db
docker compose up -d
```

### Inspect the database directly

**Inside the container:**
```bash
docker exec -it <container-name> sh
sqlite3 /app/data/app.db
```

**From the host (Compose bind mount only):**

Open `./data/app.db` with [DB Browser for SQLite](https://sqlitebrowser.org/) or any SQLite-compatible tool.

### Upgrading

```bash
docker compose pull
docker compose up -d
```

The new image will apply any pending schema migrations on startup automatically.

## Configuration

| Variable | Default | Description |
|---|---|---|
| `DB_PATH` | `./data/app.db` | SQLite database path |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |

## API

| Method | Path | Description |
|---|---|---|
| GET | `/api/health` | Health check |
| GET | `/api/items` | List all items |
| GET | `/api/items/{id}` | Get item by ID |
| POST | `/api/items` | Create item |
| PUT | `/api/items/{id}` | Update item |
| DELETE | `/api/items/{id}` | Delete item |

## Project Structure

```
src/
  porganizer.Api/        # ASP.NET Core REST API (feature-based vertical slices)
  porganizer.Database/   # EF Core DbContext, entities, migrations
  porganizer.Frontend/   # Vue 3 SPA
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
