# Location Tracker

A tiny web app: a visitor opens the page, taps **Share my location**, and the
browser shows its native permission prompt. Only after they *allow* it, the
device's current position is POSTed to the API and stored in the database.

```
Browser (getCurrentPosition)  ->  POST /api/locations  ->  EF Core  ->  PostgreSQL
        page: wwwroot/index.html          Program.cs        AppDbContext   LocationPings
```

Nothing is read or sent until the user grants permission. No background or
repeated tracking - one position per button press.

## Stack

- .NET 9 minimal API
- EF Core 9 + PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- Static HTML/JS front-end (no build step)
- Migrations applied automatically on startup

## Data model - `LocationPings` table

| column | notes |
| --- | --- |
| `Id` | bigint identity PK |
| `ClientId` | random id kept in the browser's localStorage (groups repeat visits) |
| `Latitude`, `Longitude` | required |
| `AccuracyMeters`, `AltitudeMeters`, `AltitudeAccuracyMeters`, `Heading`, `SpeedMetersPerSecond` | optional, as reported by the browser |
| `DeviceTimestampUtc` | when the browser fixed the position |
| `UserAgent`, `IpAddress` | request metadata (IP taken from `CF-Connecting-IP` / `X-Forwarded-For` when proxied) |
| `CreatedAtUtc` | server insert time |

## Endpoints

| method | path | purpose |
| --- | --- | --- |
| `GET` | `/` | the check-in page |
| `GET` | `/health` | liveness probe |
| `POST` | `/api/locations` | store one position (JSON body, see `Dtos/LocationPingRequest`) |

## Configuration

Connection string resolution order:

1. `DATABASE_URL` env var - accepts a libpq URL (`postgres://user:pass@host:port/db`), which is what Render provides.
2. `ConnectionStrings:Default` (or `ConnectionStrings__Default` env var) - a raw Npgsql key/value string.

## Run locally

Requires the .NET 9 SDK and a PostgreSQL server. Default `appsettings.json`:

```
Host=localhost;Port=5432;Database=locationtracker;Username=postgres;Password=postgres
```

```bash
dotnet run --urls http://localhost:5080
```

Open http://localhost:5080, allow location access, then:

```sql
SELECT * FROM "LocationPings" ORDER BY "Id" DESC;
```

## Deploy to Render (free, permanent)

`Dockerfile` + `render.yaml` provision a **free PostgreSQL** and a **free Docker
web service**, wired together via `DATABASE_URL`.

1. Render dashboard -> **New + -> Blueprint**
2. Pick the `LocationTracker` repo -> **Apply**
3. Wait for the build; open the service URL.

Migrations run on startup, so the `LocationPings` table is created automatically.
The free web service sleeps after ~15 min idle and wakes on the next request.

## Public link without deploying (local app + Cloudflare quick tunnel)

The browser Geolocation API needs a secure origin (`https` or `localhost`).

```bash
dotnet run --urls http://localhost:5080          # terminal 1
cloudflared tunnel --url http://localhost:5080    # terminal 2 -> prints an https URL
```

The tunnel URL lives only while both commands run and changes on restart.
