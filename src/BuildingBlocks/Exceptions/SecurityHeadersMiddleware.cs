using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Exceptions;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // OWASP Recommended Security Headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none';";

        await _next(context);
    }
}

public static class SecuritySanitizer
{
    public static string SanitizeInput(string? input, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var trimmed = input.Trim();
        if (trimmed.Length > maxLength)
        {
            trimmed = trimmed.Substring(0, maxLength);
        }

        // Prevent Path Traversal & Script/HTML Injections
        if (trimmed.Contains("../") || trimmed.Contains("..\\") || trimmed.Contains("<script") || trimmed.Contains("javascript:"))
        {
            throw new ArgumentException("Potentially unsafe characters detected in input.");
        }

        return trimmed;
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseWebShopSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
