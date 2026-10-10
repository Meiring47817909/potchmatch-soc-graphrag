using System.Text;
using Consul;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RagService.Clients;
using RagService.Logging;
using RagService.Options;
using RagService.Pipeline;
using Serilog;
using Serilog.Filters;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Http.BatchFormatters;

var builder = WebApplication.CreateBuilder(args);

// Surface Serilog sink failures (e.g. SocService rejecting the ingest key) instead of retrying silently.
Serilog.Debugging.SelfLog.Enable(Console.Error);
Directory.CreateDirectory(builder.Configuration["SocIngest:BufferPath"] ?? "/tmp/soc-buffer");

// Structured JSON logging to stdout; security events are also shipped (durably) to SocService.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "rag-service")
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty("SecurityEvent"))
        .WriteTo.DurableHttpUsingFileSizeRolledBuffers(
            requestUri: context.Configuration["SocIngest:Url"] ?? "http://soc-service:8080/api/events",
            bufferBaseFileName: (context.Configuration["SocIngest:BufferPath"] ?? "/tmp/soc-buffer") + "/events",
            batchFormatter: new ArrayBatchFormatter(),
            httpClient: new SocIngestHttpClient(context.Configuration))));

var consulAddress = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
builder.Services.AddSingleton<IConsulClient, ConsulClient>(_ =>
    new ConsulClient(config => { config.Address = new Uri(consulAddress); }));

builder.Services.AddHealthChecks();

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<GraphQueryOptions>(builder.Configuration.GetSection("GraphQuery"));

builder.Services.AddHttpClient<IGeminiClient, GeminiClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});
builder.Services.AddHttpClient<IGraphQueryClient, GraphQueryClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<GraphQueryOptions>>().Value;
    http.BaseAddress = new Uri(o.Url.EndsWith('/') ? o.Url : o.Url + "/");
    http.Timeout = TimeSpan.FromSeconds(20);
});

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
    });

builder.Services.AddAuthorization();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<RagPipeline>();

// Question validation (empty / > 500 chars) is done in RagController so every 400 has the
// { error: "invalid_question", message } body; the implicit [Required] on non-nullable strings would
// otherwise short-circuit an empty question into a ProblemDetails response.
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

var app = builder.Build();

// Register with Consul
var consulClient = app.Services.GetRequiredService<IConsulClient>();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var serviceId = $"rag-service-{Guid.NewGuid()}";

appLifetime.ApplicationStarted.Register(() =>
{
    var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var listeningUrl = new Uri(addressFeature!.Addresses.First());

    var serviceAddress = builder.Configuration["ServiceRegistration:Address"] ?? "localhost";
    var healthCheckHost = builder.Configuration["ServiceRegistration:HealthCheckHost"] ?? "host.docker.internal";

    consulClient.Agent.ServiceRegister(new AgentServiceRegistration
    {
        ID = serviceId,
        Name = "rag-service",
        Address = serviceAddress,
        Port = listeningUrl.Port,
        Check = new AgentServiceCheck
        {
            HTTP = $"http://{healthCheckHost}:{listeningUrl.Port}/health",
            Interval = TimeSpan.FromSeconds(10),
            Timeout = TimeSpan.FromSeconds(5),
            DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1),
        }
    }).Wait();
});

appLifetime.ApplicationStopping.Register(() =>
{
    consulClient.Agent.ServiceDeregister(serviceId).Wait();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
