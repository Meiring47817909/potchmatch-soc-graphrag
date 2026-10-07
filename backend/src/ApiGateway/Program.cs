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

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout - see BookingService/Program.cs (and
// SecurityEventLog.cs in this project) for the full rationale. This is the
// single entry point for every client request in the whole system, so it's
// also the single best place to log every authentication failure Ocelot
// rejects before a request ever reaches a downstream service.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "api-gateway")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

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
            OnAuthenticationFailed = authContext =>
            {
                var logger = authContext.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                SecurityEventLog.Emit(logger, LogLevel.Warning, "AUTH_TOKEN_REJECTED", "MEDIUM",
                    authContext.Exception.Message,
                    sourceIp: authContext.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    endpoint: authContext.HttpContext.Request.Path,
                    httpMethod: authContext.HttpContext.Request.Method);
                return Task.CompletedTask;
            },
            OnChallenge = challengeContext =>
            {
                var logger = challengeContext.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                SecurityEventLog.Emit(logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                    "Request to protected endpoint rejected: " + (challengeContext.ErrorDescription ?? challengeContext.Error ?? "no valid token"),
                    sourceIp: challengeContext.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    endpoint: challengeContext.HttpContext.Request.Path,
                    httpMethod: challengeContext.HttpContext.Request.Method,
                    statusCode: 401);
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
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        SecurityEventLog.Emit(logger, LogLevel.Information, "REQUEST_RECEIVED", "LOW",
            $"{context.Request.Method} {context.Request.Path} received",
            sourceIp: context.Connection.RemoteIpAddress?.ToString(),
            endpoint: context.Request.Path,
            httpMethod: context.Request.Method);

        await next();
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
