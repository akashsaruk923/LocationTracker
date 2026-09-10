# Make the site permanent (fixed URL, always on, free)

Two one-time steps. After this the site auto-starts on every boot with the same
URL and no terminal open.

## 1. Fixed free URL with ngrok

1. Sign up (free, ~2 min): https://dashboard.ngrok.com/signup  (Google/GitHub login works)
2. Install ngrok:  `winget install ngrok.ngrok`  (or download from ngrok.com/download)
3. Copy your authtoken from https://dashboard.ngrok.com/get-started/your-authtoken and run:
   ```
   ngrok config add-authtoken <YOUR_AUTHTOKEN>
   ```
4. Claim your free static domain: https://dashboard.ngrok.com/domains -> **New Domain**.
   You get something like `akash-location.ngrok-free.app` - it is yours permanently.
5. Put that domain (just the host, no `https://`) in a file next to this one:
   ```
   echo akash-location.ngrok-free.app > ngrok-domain.txt
   ```

`serve.ps1` now uses that fixed URL. Without these steps it falls back to a
random Cloudflare URL that changes each run.

## 2. Auto-start on boot

In PowerShell:

```
powershell -ExecutionPolicy Bypass -File C:\Users\hp\projects\LocationTracker\install-task.ps1
schtasks /run /tn LocationTracker
```

Now:
- app + tunnel start at every logon, no window needed
- either one auto-restarts if it stops
- the live URL is always in `current-url.txt`

Stop / remove: `schtasks /delete /tn LocationTracker /f`

## Notes

- The PC must be powered on and you must be logged in for the site to serve
  (it is hosted on this machine so it can reach your local SQL Server).
- ngrok free static domain: 1 domain, 1 GB/month transfer - fine for this.
- Data goes to `LocationTrackerDb` on your local SQL Server either way.
