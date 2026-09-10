# Location Tracker

A tiny web app. The instant a visitor opens the link, an **approximate
(city-level) location is saved from their IP address** - no permission needed.
The page then immediately asks for **precise** location; if the visitor allows
the browser prompt, the exact device position is saved too.

```
page load ─┬─> POST /api/visit     ─> IP lookup (ipwho.is) ─┐
           │                                                 ├─> EF Core ─> PostgreSQL
           └─> getCurrentPosition ──> POST /api/locations ──┘        (LocationPings)
```

Every row carries a ready-to-open `GoogleMapsUrl`. Rows are tagged
`Source = "ip"` (approximate, automatic) or `Source = "gps"` (precise, consented).

> A browser **cannot** hand over precise GPS without the visitor accepting the
> permission prompt - that is a hard security rule of the web platform. What the
> page does is fire that prompt automatically on load (no button), and on repeat
> visits where permission was already granted it saves silently. The IP-based
> row is the fallback that is always saved.

## Stack

- .NET 9 minimal API
- EF Core 9 + PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- Static HTML/JS front-end (no build step)
- Migrations applied automatically on startup

## Data model - `LocationPings` table

| column | notes |
| --- | --- |
| `Id` | bigint identity PK |
| `Source` | `"ip"` (approximate, automatic) or `"gps"` (precise, consented) |
| `ClientId` | random id kept in the browser's localStorage (groups repeat visits) |
| `Latitude`, `Longitude` | nullable - an IP lookup can fail, a denied prompt leaves them empty |
| `GoogleMapsUrl` | `https://www.google.com/maps?q=<lat>,<lng>` for the row |
| `City`, `Region`, `Country` | from the IP lookup |
| `AccuracyMeters`, `AltitudeMeters`, `AltitudeAccuracyMeters`, `Heading`, `SpeedMetersPerSecond` | GPS rows only, as reported by the browser |
| `CreatedAtIst` | **server insert time in India Standard Time (UTC+5:30)** - the column to read |
| `CreatedAtUtc` | same moment in UTC (the true instant) |
| `DeviceTimestampIst` / `DeviceTimestampUtc` | when the browser fixed the position, IST / UTC |
| `UserAgent`, `IpAddress` | request metadata (IP taken from `CF-Connecting-IP` / `X-Forwarded-For` when proxied) |

## Endpoints

| method | path | purpose |
| --- | --- | --- |
| `GET` | `/` | the check-in page |
| `GET` | `/health` | liveness probe |
| `POST` | `/api/visit` | `{ "clientId": "..." }` - save the IP-based approximate location |
| `POST` | `/api/locations` | precise position (JSON body, see `Dtos/LocationPingRequest`) |
| `GET` | `/api/recent?key=<ADMIN_KEY>` | last 50 rows as JSON; 401 unless the `ADMIN_KEY` env var is set |

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
SELECT "Id", "Source", "City", "Country", "Latitude", "Longitude", "GoogleMapsUrl", "CreatedAtUtc"
FROM "LocationPings" ORDER BY "Id" DESC;
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
