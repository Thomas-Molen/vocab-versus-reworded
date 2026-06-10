using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Wordset.Application.Services;
using Wordset.API.Endpoints;
using Wordset.API.GrpcServices;
using Wordset.Infrastructure;
using Wordset.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddScoped<WordsetService>();
builder.Services.AddScoped<WordService>();
builder.Services.AddScoped<WordGameService>();
builder.Services.AddOpenApi();
builder.Services.AddGrpc();

builder.WebHost.ConfigureKestrel(options =>
{
    // REST (HTTP/1.1 + HTTP/2)
    options.ListenAnyIP(8080);
    // gRPC (HTTP/2 cleartext — internal service mesh only)
    options.ListenAnyIP(8090, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

var app = builder.Build();

// Apply pending migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WordsetDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapWordsetEndpoints();
app.MapWordEndpoints();
app.MapGrpcService<WordsetGameGrpcService>();

app.Run();

// Expose for WebApplicationFactory in tests
public partial class Program { }
