using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PaymentService.Data;
using PaymentService.Logging;

// Register with Consul on startup, deregister on shutdown
using Consul;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Http.BatchFormatters;
using Serilog.Filters;

// Consul's own client library also defines a LogLevel type (Consul.LogLevel),
// which collides with the ASP.NET Core one this file actually uses for its
// structured-logging calls - this alias picks the right one explicitly
// instead of leaving `LogLevel` ambiguous between the two.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout - see IdentityService/Program.cs for the
// full rationale. This service is the far end of BookingService's
// "inter-service call" (holding/releasing/refunding escrow), so its
// REQUEST_RECEIVED/REQUEST_COMPLETED lines for /api/payments/hold, tagged
// with the SAME CorrelationId BookingService logged its own call under
// (via BookingService's CorrelationPropagatingHandler), are what actually
// let that call be traced end-to-end across both services' logs.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "payment-service")
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

// Register with Consul. Consul:Address lets docker-compose point this at the
// "consul" container by name instead of localhost - defaults to localhost so
// `dotnet run` on the host still works exactly as before.
var consulAddress = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
builder.Services.AddSingleton<IConsulClient, ConsulClient>(_ =>
    new ConsulClient(config => { config.Address = new Uri(consulAddress); }));

// For /health endpoints
builder.Services.AddHealthChecks();

// Add services to the container.
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

var jwtKey = builder.Configuration["Jwt:Key"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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

builder.Services.AddAuthorization();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Behind the Gateway, the TCP peer is the Gateway container. Trust the
// X-Forwarded-For it sets ({RemoteIpAddress} via Ocelot) so Connection.RemoteIpAddress
// is the real client. Networks/proxies are cleared because the Gateway's Docker IP isn't fixed.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Apply any pending EF Core migrations automatically on startup, so a fresh
// database (e.g. a brand-new Postgres container in docker-compose) gets its
// schema created without a separate manual `dotnet ef database update` step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    db.Database.Migrate();
}

// Register service with Consul
var consulClient = app.Services.GetRequiredService<IConsulClient>();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var serviceId = $"payment-service-{Guid.NewGuid()}";

appLifetime.ApplicationStarted.Register(() =>
{
    var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var listeningUrl = new Uri(addressFeature!.Addresses.First());

    // "localhost" only works when the Gateway/Consul run on the same host as this
    // service does. Inside docker-compose everything runs in its own container, so
    // ServiceRegistration:Address/HealthCheckHost let compose override both to this
    // container's own service name (e.g. "payment-service") - other containers on
    // the same compose network can reach it by that name directly.
    var serviceAddress = builder.Configuration["ServiceRegistration:Address"] ?? "localhost";
    var healthCheckHost = builder.Configuration["ServiceRegistration:HealthCheckHost"] ?? "host.docker.internal";

    consulClient.Agent.ServiceRegister(new AgentServiceRegistration
    {
        ID = serviceId,
        Name = "payment-service",
        Address = serviceAddress,
        Port = listeningUrl.Port,
        Check = new AgentServiceCheck
        {
            HTTP = $"http://{healthCheckHost}:{listeningUrl.Port}/health",
            Interval = TimeSpan.FromSeconds(10),
            Timeout = TimeSpan.FromSeconds(5),
        }
    }).Wait();
});

appLifetime.ApplicationStopping.Register(() =>
{
    consulClient.Agent.ServiceDeregister(serviceId).Wait();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// For /health endpoints
app.MapHealthChecks("/health");

// Correlation ID: reuse the one BookingService's CorrelationPropagatingHandler
// (or the Gateway) already set, or mint a new one if this endpoint was hit
// directly. Pushed into Serilog's LogContext so every log line emitted
// anywhere during this request carries it automatically.
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
    }
});

// Unhandled exceptions: log with full detail before the default error
// handling takes over, so a stack trace always lands in the structured logs
// rather than only ever being visible in a browser's Network tab / Postman.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        SecurityEventLog.Emit(logger, LogLevel.Error, "UNHANDLED_EXCEPTION", "HIGH", ex.Message,
            endpoint: context.Request.Path, httpMethod: context.Request.Method);
        throw;
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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
