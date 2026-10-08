using System.Text;
using Consul;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Serilog;
using Serilog.Formatting.Compact;
using GraphService.Graph;
using GraphService.Seeding;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout, same convention as the other services.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "graph-service")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Consul:Address lets docker-compose point this at the "consul" container;
// defaults to localhost so `dotnet run` on the host still works.
var consulAddress = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
builder.Services.AddSingleton<IConsulClient, ConsulClient>(_ =>
    new ConsulClient(config => { config.Address = new Uri(consulAddress); }));

builder.Services.AddHealthChecks();

builder.Services.AddSingleton<IDriver>(_ => GraphDatabase.Driver(
    builder.Configuration["Neo4j:Uri"] ?? "bolt://localhost:7687",
    AuthTokens.Basic(
        builder.Configuration["Neo4j:User"] ?? "neo4j",
        builder.Configuration["Neo4j:Password"] ?? "potchmatch-dev")));
builder.Services.AddSingleton<Neo4jConnector>();
builder.Services.AddSingleton<SeedLoader>();
builder.Services.AddSingleton<GraphStartupGate>();
builder.Services.AddHostedService<GraphStartupService>();

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

builder.Services.AddControllers();

var app = builder.Build();

// Register with Consul
var consulClient = app.Services.GetRequiredService<IConsulClient>();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var serviceId = $"graph-service-{Guid.NewGuid()}";

appLifetime.ApplicationStarted.Register(() =>
{
    var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var listeningUrl = new Uri(addressFeature!.Addresses.First());

    var serviceAddress = builder.Configuration["ServiceRegistration:Address"] ?? "localhost";
    var healthCheckHost = builder.Configuration["ServiceRegistration:HealthCheckHost"] ?? "host.docker.internal";

    consulClient.Agent.ServiceRegister(new AgentServiceRegistration
    {
        ID = serviceId,
        Name = "graph-service",
        Address = serviceAddress,
        Port = listeningUrl.Port,
        Check = new AgentServiceCheck
        {
            HTTP = $"http://{healthCheckHost}:{listeningUrl.Port}/health",
            Interval = TimeSpan.FromSeconds(10),
            Timeout = TimeSpan.FromSeconds(5),
            // Drop registrations left behind by a non-graceful exit (kill/crash).
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
