using System.Security.Claims;
using System.Text;
using BookingService.Data;
using BookingService.Http;
using BookingService.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Register with Consul on startup, deregister on shutdown
using Consul;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// Circuit Breaker/Retry pattern
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

// Consul's own client library also defines a LogLevel type (Consul.LogLevel),
// which collides with the ASP.NET Core one this file actually uses for its
// structured-logging calls - this alias picks the right one explicitly
// instead of leaving `LogLevel` ambiguous between the two.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout - see IdentityService/Program.cs for the
// full rationale. This service is the one making the "inter-service call" to
// PaymentService (below), so its logs are also where that call's own
// request/response gets an explicit INTER_SERVICE_CALL event, on top of the
// generic request-received/completed pair every service gets.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "booking-service")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Register with Consul. Consul:Address lets docker-compose point this at the
// "consul" container by name instead of localhost - defaults to localhost so
// `dotnet run` on the host still works exactly as before.
var consulAddress = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
builder.Services.AddSingleton<IConsulClient, ConsulClient>(_ =>
    new ConsulClient(config => { config.Address = new Uri(consulAddress); }));

// For /health endpoints
builder.Services.AddHealthChecks();

// Add services to the container.
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookingDb")));

var jwtKey = builder.Configuration["Jwt:Key"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["jwt:Audience"],
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

// Needed so CorrelationPropagatingHandler (below) can read the current
// request's own X-Correlation-Id header when it's invoked from inside the
// "PaymentService" HttpClient pipeline, outside of any controller.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationPropagatingHandler>();

// Register an HttpClient for talking to PaymentService
builder.Services.AddHttpClient("PaymentService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:PaymentServiceBaseUrl"]!);
})
.AddHttpMessageHandler<CorrelationPropagatingHandler>()
.AddResilienceHandler("PaymentServicePipeline", pipelineBuilder =>
{
    // Only treat this as a failure worth retrying/circuit-breaking when it's an
    // actual infrastructure problem - a transport-level exception or a 5xx from
    // PaymentService itself. A 4xx like 402 Insufficient Funds or 409 Duplicate
    // Hold is a legitimate, deterministic business answer that BookingController
    // already handles explicitly (see Accept()) - retrying it just gets the same
    // 402 again, and three of those in a row was enough to trip the circuit
    // breaker and mask the real "insufficient funds" answer behind a generic
    // "payment service unavailable" 503. Retrying/breaking on 5xx and transport
    // failures only avoids that false positive.
    var isInfrastructureFailure = new PredicateBuilder<HttpResponseMessage>()
        .Handle<HttpRequestException>()
        .HandleResult(response => (int)response.StatusCode >= 500);

    // Retry: for short-lived blips - try again a few times with a growing delay
    // between attempts, in case PaymentService just hiccuped.
    pipelineBuilder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(500),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = isInfrastructureFailure
    });

    // Circuit Breaker: for sustained failures - if PaymentService keeps failing,
    // stop hammering it and fail fast for a cooldown period instead.
    pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(10),
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(15),
        ShouldHandle = isInfrastructureFailure
    });

    // Per-attempt timeout - don't let any single call hang forever.
    pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(5));
});

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums (BookingStatus) as their string names ("Open", "Accepted", ...)
        // instead of raw integers - the Angular frontend's TypeScript types are string
        // unions ('Open' | 'Accepted' | ...) that expect to match these directly.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply any pending EF Core migrations automatically on startup, so a fresh
// database (e.g. a brand-new Postgres container in docker-compose) gets its
// schema created without a separate manual `dotnet ef database update` step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    db.Database.Migrate();
}

// Register service with Consul
var consulClient = app.Services.GetRequiredService<IConsulClient>();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var serviceId = $"booking-service-{Guid.NewGuid()}";

appLifetime.ApplicationStarted.Register(() =>
{
    var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var listeningUrl = new Uri(addressFeature!.Addresses.First());

    // "localhost" only works when the Gateway/Consul run on the same host as this
    // service does. Inside docker-compose everything runs in its own container, so
    // ServiceRegistration:Address/HealthCheckHost let compose override both to this
    // container's own service name (e.g. "booking-service") - other containers on
    // the same compose network can reach it by that name directly.
    var serviceAddress = builder.Configuration["ServiceRegistration:Address"] ?? "localhost";
    var healthCheckHost = builder.Configuration["ServiceRegistration:HealthCheckHost"] ?? "host.docker.internal";

    consulClient.Agent.ServiceRegister(new AgentServiceRegistration
    {
        ID = serviceId,
        Name = "booking-service",
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

// Correlation ID: reuse the one the Gateway (or BookingController's caller)
// already set, or mint a new one if this endpoint was hit directly. Pushed
// into Serilog's LogContext so every log line emitted anywhere during this
// request - including BookingController's own INTER_SERVICE_CALL events -
// carries it automatically.
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
