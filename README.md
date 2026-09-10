# Location Tracker

A tiny web app. The instant a visitor opens the link, an **approximate
(city-level) location is saved from their IP address** - no permission needed.
The page then immediately asks for **precise** location; if the visitor allows
the browser prompt, the exact device position is saved too.

```
page load ─┬─> POST /api/visit     ─> IP lookup (ipwho.is) ─┐
           │                                                 ├─> EF Core ─> SQL Server
           └─> getCurrentPosition ──> POST /api/locations ──┘        (LocationPings)
```

Every row carries a ready-to-open `GoogleMapsUrl`. Rows are tagged
`Source = "ip"` (approximate, automatic) or `Source = "gps"` (precise, consented).

> A browser **cannot** hand over precise GPS without the visitor accepting the
> permission prompt - that is a hard security rule of the web platform. The page
> fires that prompt automatically on load (no button); on repeat visits where
> permission was already granted it saves silently. The IP-based row is the
> fallback that is always saved.

## Stack

- .NET 9 minimal API
- EF Core 9 + **SQL Server** (`Microsoft.EntityFrameworkCore.SqlServer`)
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

The connection string comes from `ConnectionStrings:Default` in
`appsettings.json`, or the `ConnectionStrings__Default` environment variable.
Default (local SQL Server, Windows auth, DB created on first run):

```
Server=localhost;Database=LocationTrackerDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False
```

## Run + publish a public link

The browser Geolocation API needs a secure origin (`https` or `localhost`), and
the app must reach your SQL Server - so it runs on your machine and is exposed
with a Cloudflare quick tunnel (public HTTPS, no account, no domain):

```bash
# terminal 1 - the app (writes to your local SQL Server)
dotnet run -c Release --urls http://localhost:5080

# terminal 2 - the public link
cloudflared tunnel --url http://localhost:5080
```

`cloudflared` prints a `https://<random>.trycloudflare.com` URL - share that.
It lives only while both commands run and changes on every restart. For a stable
URL, run a Cloudflare *named* tunnel (needs a domain on Cloudflare) or host the
app on a machine that stays on.

Check the data:

```sql
SELECT TOP 50 Id, Source, City, Country, Latitude, Longitude, GoogleMapsUrl, CreatedAtIst
FROM LocationPings
ORDER BY Id DESC;
```

`Dockerfile` is kept for containerised hosting, but note SQL Server itself is not
included - point `ConnectionStrings__Default` at a reachable SQL Server.
