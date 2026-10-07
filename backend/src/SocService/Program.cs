using Consul;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using SocService.Data;
using SocService.Detection;
using SocService.Ingestion;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging to stdout, same convention as the other services.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "soc-service")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Consul:Address lets docker-compose point this at the "consul" container;
// defaults to localhost so `dotnet run` on the host still works.
var consulAddress = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
builder.Services.AddSingleton<IConsulClient, ConsulClient>(_ =>
    new ConsulClient(config => { config.Address = new Uri(consulAddress); }));

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<SocDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SocDb")));

builder.Services.AddScoped<EventIngestionService>();

builder.Services.Configure<DetectionOptions>(builder.Configuration.GetSection(DetectionOptions.SectionName));
builder.Services.AddScoped<AlertWriter>();
builder.Services.AddScoped<DetectionEngine>();
builder.Services.AddHostedService<DetectionWorker>();

builder.Services.AddControllers();

var app = builder.Build();

// Apply pending migrations on startup so a fresh database gets its schema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SocDbContext>();
    db.Database.Migrate();
}

// Register with Consul
var consulClient = app.Services.GetRequiredService<IConsulClient>();
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var serviceId = $"soc-service-{Guid.NewGuid()}";

appLifetime.ApplicationStarted.Register(() =>
{
    var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var listeningUrl = new Uri(addressFeature!.Addresses.First());

    var serviceAddress = builder.Configuration["ServiceRegistration:Address"] ?? "localhost";
    var healthCheckHost = builder.Configuration["ServiceRegistration:HealthCheckHost"] ?? "host.docker.internal";

    consulClient.Agent.ServiceRegister(new AgentServiceRegistration
    {
        ID = serviceId,
        Name = "soc-service",
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

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
