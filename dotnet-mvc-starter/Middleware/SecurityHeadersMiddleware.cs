namespace api.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        return _next(ctx);
    }
}
