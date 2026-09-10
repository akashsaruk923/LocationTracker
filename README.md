# Location Tracker

A tiny web app: a visitor opens the page, taps **Share my location**, and the
browser shows its native permission prompt. Only after they *allow* it, the
device's current position is POSTed to the API and stored in **SQL Server**.

```
Browser (getCurrentPosition)  ->  POST /api/locations  ->  EF Core  ->  SQL Server
        page: wwwroot/index.html          Program.cs        AppDbContext   LocationTrackerDb
```

Nothing is read or sent until the user grants permission. No background or
repeated tracking - one position per button press.

## Stack

- .NET 9 minimal API
- EF Core 9 + SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`)
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
| `UserAgent`, `IpAddress` | request metadata (IP taken from `CF-Connecting-IP` / `X-Forwarded-For` when tunnelled) |
| `CreatedAtUtc` | server insert time |

## Run locally

Requires the .NET 9 SDK and a reachable SQL Server. The default connection
string (in `appsettings.json`) points at a local default instance with Windows
auth and creates the `LocationTrackerDb` database on first run:

```
Server=localhost;Database=LocationTrackerDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False
```

```bash
dotnet run --urls http://localhost:5080
```

Open http://localhost:5080 and allow location access. Check the data:

```sql
SELECT * FROM LocationTrackerDb.dbo.LocationPings ORDER BY Id DESC;
```

Override the database without editing files:

```bash
setx ConnectionStrings__Default "Server=...;Database=LocationTrackerDb;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True"
```

## Endpoints

| method | path | purpose |
| --- | --- | --- |
| `GET` | `/` | the check-in page |
| `GET` | `/health` | liveness probe |
| `POST` | `/api/locations` | store one position (JSON body, see `Dtos/LocationPingRequest`) |

## Publish a public link - local app + Cloudflare quick tunnel

The browser Geolocation API needs a secure origin (`https` or `localhost`).
A Cloudflare quick tunnel gives you public HTTPS with no account and no domain,
and the app keeps writing to your **local** SQL Server:

```bash
# terminal 1
dotnet run --urls http://localhost:5080

# terminal 2
cloudflared tunnel --url http://localhost:5080
```

`cloudflared` prints a `https://<random>.trycloudflare.com` URL - share that.
The link lives only while both commands run and changes on restart.

## Deploy the app to Render

`Dockerfile` + `render.yaml` are included. In the Render dashboard:
**New -> Blueprint -> this repo**, then set `ConnectionStrings__Default`
(Environment tab, secret) to a SQL Server that Render can reach over the network.

Render's network cannot reach a SQL Server on your PC unless that port is
exposed with a **TCP** tunnel (a Cloudflare *named* tunnel needs a domain on
Cloudflare; quick tunnels are HTTP-only). For a permanent cloud database use
**Azure SQL Database (free tier)** and paste its connection string.
