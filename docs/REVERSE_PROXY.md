# Reverse Proxy: nginx Rate-Limit Recipe

This API ships **without** app-level rate limiting. Production deployments are expected to sit behind nginx (or Traefik / Cloudflare / AWS WAF) which handles throttling, TLS termination, and access logging.

## Why proxy-level

- nginx serves throttling far cheaper than ASP.NET middleware.
- Centralised across multiple app instances (no per-pod counter divergence).
- Rules editable without redeploying app.

## Drop-in nginx config

```nginx
# /etc/nginx/conf.d/api.conf

# Memory zones (10 MB ≈ 160k IPs)
limit_req_zone $binary_remote_addr zone=api_general:10m rate=100r/m;
limit_req_zone $binary_remote_addr zone=api_auth:10m    rate=5r/m;
limit_req_zone $binary_remote_addr zone=api_upload:10m  rate=10r/m;

upstream backend {
    server backend:8080;
    keepalive 32;
}

server {
    listen 443 ssl http2;
    server_name api.example.com;

    ssl_certificate     /etc/letsencrypt/live/api.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.example.com/privkey.pem;

    # Pass real client IP downstream
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Real-IP       $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Host            $host;

    # --- Strict throttle: auth endpoints (anti brute-force) ---
    location ~ ^/api/auth/(login|register) {
        limit_req zone=api_auth burst=3 nodelay;
        proxy_pass http://backend;
    }

    # --- Upload throttle + body size cap ---
    location /api/profiles/me/redactpfp {
        limit_req zone=api_upload burst=5 nodelay;
        client_max_body_size 6m;            # avatar max 5MB + multipart overhead
        proxy_pass http://backend;
    }

    # --- Health probe: bypass throttling ---
    location = /health/live {
        access_log off;
        proxy_pass http://backend;
    }

    # --- Default: everything else ---
    location / {
        limit_req zone=api_general burst=20 nodelay;
        proxy_pass http://backend;
    }
}
```

## How `limit_req` works

- `zone=name:size rate=N r/m` — shared memory bucket + replenish rate.
- `burst=B nodelay` — allow B requests above rate as instant burst; reject anything beyond.
- Without `nodelay`, excess requests queue (latency penalty); with — they 503 immediately.

## Tuning hints

| Endpoint | Suggested rate | Rationale |
|----------|---------------|-----------|
| `/api/auth/login` | 5/min/IP | Brute-force resistance |
| `/api/auth/register` | 5/min/IP | Spam account creation |
| `/api/profiles/me/redactpfp` | 10/min/IP | Disk + bandwidth budget |
| General | 100/min/IP | Normal usage headroom |

For per-user (not per-IP) limits behind shared NAT, use `$http_authorization` as the key — but it requires the JWT to be present, so combine with IP fallback.

## App side (optional)

If the app needs to know the real client IP (logs, abuse tracking), enable forwarded headers in `Configure.cs`:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
// pipeline, BEFORE UseRouting:
app.UseForwardedHeaders();
```

**Caveat:** in untrusted networks, configure `KnownProxies` to the actual proxy IP, otherwise an external client can spoof `X-Forwarded-For` and bypass IP-based logic.

## Cloudflare / Traefik equivalents

- **Cloudflare**: configure WAF rate-limiting rules per URI in dashboard. Same logic — burst + replenish.
- **Traefik**: `middlewares.ratelimit` plugin with `average` + `burst` knobs.

## Testing

```bash
# Hammer login — expect 429 after 5
for i in {1..10}; do
  curl -s -o /dev/null -w "%{http_code}\n" \
    -X POST https://api.example.com/api/auth/login \
    -H 'content-type: application/json' \
    -d '{"email":"x@y.z","password":"bad"}'
done
```

Expected: first ~5 = `400`, rest = `503` (nginx limit_req_status default).
