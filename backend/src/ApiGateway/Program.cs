using System.Security.Claims;
using System.Text;
using ApiGateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Consul;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Http.BatchFormatters;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout - see BookingService/Program.cs (and
// SecurityEventLog.cs in this project) for the full rationale. This is the
// single entry point for every client request in the whole system, so it's
// also the single best place to log every authentication failure Ocelot
// rejects before a request ever reaches a downstream service.
// Surface Serilog sink failures (e.g. SocService rejecting the ingest key) in
// `docker compose logs` instead of the durable sink retrying silently.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// The durable sink's shipper polls <dir>/events.bookmark and fails (until the
// first event creates the directory) if the directory doesn't exist yet.
Directory.CreateDirectory(builder.Configuration["SocIngest:BufferPath"] ?? "/tmp/soc-buffer");

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "api-gateway")
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    // Ship security events to SocService. Durable: events are buffered on disk
    // and retried if SocService is down. Buffer location is configurable via
    // SocIngest:BufferPath (default /tmp/soc-buffer - writable in the container).
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty("SecurityEvent"))
        .WriteTo.DurableHttpUsingFileSizeRolledBuffers(
            requestUri: context.Configuration["SocIngest:Url"] ?? "http://soc-service:8080/api/events",
            bufferBaseFileName: (context.Configuration["SocIngest:BufferPath"] ?? "/tmp/soc-buffer") + "/events",
            batchFormatter: new ArrayBatchFormatter(),
            httpClient: new SocIngestHttpClient(context.Configuration))));

builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
// In docker-compose, ASPNETCORE_ENVIRONMENT=Docker triggers this optional overlay
// (ocelot.Docker.json) which points ServiceDiscoveryProvider at the "consul"
// container by name instead of localhost. Host-run `dotnet run` never sets that
// environment variable, so this file is simply skipped and behaviour is unchanged.
builder.Configuration.AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

var jwtKey = builder.Configuration["Jwt:Key"]!;

/*
Note the string "SquashPlatformAuth" here matches the AuthenticationProviderKey in ocelot.json exactly
— that's how Ocelot knows which auth scheme to enforce on which routes.
*/

builder.Services.AddAuthentication()
    .AddJwtBearer("SquashPlatformAuth", options =>
    {
        // Do not remap inbound claim names: claims keep exactly the type strings they have
        // in the token (IdentityService's tokens carry the role under the long
        // ClaimTypes.Role URI; "sub" stays "sub"). The short "role" claim that ocelot.json's
        // RouteClaimsRequirement uses is added in OnTokenValidated below.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
        };
        // Logged here, not just inferred later from a 401 status code - this is
        // the exact moment "access control to protected endpoints" (spec 3.1)
        // actually rejects someone, and being the single entry point means
        // every rejected request anywhere in the system shows up here,
        // regardless of which downstream service the route targets.
        options.Events = new JwtBearerEvents
        {
            // IdentityService's tokens carry the role under the long ClaimTypes.Role URI,
            // and Ocelot cannot be configured with that as a RouteClaimsRequirement key
            // (the ':' in "http://..." is a configuration-path separator and splits the key).
            // So expose it under the short name "role" as well, for ocelot.json.
            OnTokenValidated = validated =>
            {
                if (validated.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == "role"))
                {
                    foreach (var role in identity.FindAll(ClaimTypes.Role).ToList())
                    {
                        identity.AddClaim(new Claim("role", role.Value));
                    }
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = authContext =>
            {
                // Tells the middleware below that this 401 was already logged as AUTH_TOKEN_REJECTED.
                authContext.HttpContext.Items["soc.authFailed"] = true;
                var logger = authContext.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                SecurityEventLog.Emit(logger, LogLevel.Warning, "AUTH_TOKEN_REJECTED", "MEDIUM",
                    authContext.Exception.Message,
                    sourceIp: authContext.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    endpoint: authContext.HttpContext.Request.Path,
                    httpMethod: authContext.HttpContext.Request.Method);
                return Task.CompletedTask;
            },
        };
    });

// The Angular dev server runs on a different origin (localhost:4200) to the
// Gateway (localhost:5132), so the browser blocks calls between them unless
// the Gateway explicitly allows it via CORS.
const string FrontendCorsPolicy = "AllowFrontend";

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// AddConsul<AddressPreferringConsulServiceBuilder>() instead of the plain AddConsul():
// Ocelot's default Consul service builder prefers the catalog NODE's name over the
// registered Service.Address whenever a node resolves, which breaks down as soon as
// the node name (here, "consul", the agent's own container) isn't where the actual
// service lives. See AddressPreferringConsulServiceBuilder.cs for the full story.
builder.Services.AddOcelot(builder.Configuration).AddConsul<AddressPreferringConsulServiceBuilder>();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration["SocIngest:Key"]))
{
    app.Logger.LogWarning("SocIngest:Key is not configured - SocService will reject this service's security events (401) and they will pile up in the durable buffer. Set SocIngest__Key.");
}

// Correlation ID: minted here, at the single entry point, for every request
// that enters the system. Ocelot forwards it downstream as an ordinary
// header, so BookingService/IdentityService/PaymentService's own copy of
// this same middleware picks up the SAME id instead of minting a new one -
// letting one client request be traced across every service log it touches.
const string CorrelationIdHeader = "X-Correlation-Id";
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing) && !string.IsNullOrWhiteSpace(existing)
        ? existing.ToString()
        : Guid.NewGuid().ToString();

    context.Request.Headers[CorrelationIdHeader] = correlationId;
    context.Response.Headers[CorrelationIdHeader] = correlationId;

    using (LogContext.PushProperty("CorrelationId", correlationId))
    using (LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString()))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        SecurityEventLog.Emit(logger, LogLevel.Information, "REQUEST_RECEIVED", "LOW",
            $"{context.Request.Method} {context.Request.Path} received",
            sourceIp: context.Connection.RemoteIpAddress?.ToString(),
            endpoint: context.Request.Path,
            httpMethod: context.Request.Method);

        await next();

        // 403 from the gateway itself (RouteClaimsRequirement failed: authenticated but
        // not an Admin). Only emitted when no downstream response exists, i.e. the
        // request never reached a service, so a service's own 403 is not double-counted.
        if (context.Response.StatusCode == StatusCodes.Status403Forbidden
            && context.Items.DownstreamResponse() is null)
        {
            SecurityEventLog.Emit(logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                "Forbidden: caller lacks the required role",
                userId: context.User.FindFirstValue("sub"),
                sourceIp: context.Connection.RemoteIpAddress?.ToString(),
                endpoint: context.Request.Path,
                httpMethod: context.Request.Method,
                statusCode: 403);
        }

        // 401 from the gateway itself that was not an authentication failure with a token
        // (no header, empty header, non-Bearer scheme...). (Ocelot authenticates and writes
        // this 401 itself, so JwtBearer's OnChallenge never fires - it was removed.)
        // Rejected tokens set soc.authFailed in OnAuthenticationFailed and are already
        // logged as AUTH_TOKEN_REJECTED, so they are skipped here: one event per request.
        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized
            && context.Items.DownstreamResponse() is null
            && !context.Items.ContainsKey("soc.authFailed"))
        {
            SecurityEventLog.Emit(logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                "Request to protected endpoint without a valid token",
                sourceIp: context.Connection.RemoteIpAddress?.ToString(),
                endpoint: context.Request.Path,
                httpMethod: context.Request.Method,
                statusCode: 401);
        }
    }
});

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "REQUEST_COMPLETED {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, _, ex) =>
        ex != null || httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : httpContext.Response.StatusCode >= 400 ? Serilog.Events.LogEventLevel.Warning
        : Serilog.Events.LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("SourceIp", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserId", httpContext.User.FindFirstValue("sub"));
    };
});

app.UseCors(FrontendCorsPolicy);

await app.UseOcelot();

app.Run();
