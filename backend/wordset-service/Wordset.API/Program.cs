using Microsoft.EntityFrameworkCore;
using Wordset.Application.Services;
using Wordset.API.Endpoints;
using Wordset.Infrastructure;
using Wordset.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddScoped<WordsetService>();
builder.Services.AddOpenApi();

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

app.Run();

// Expose for WebApplicationFactory in tests
public partial class Program { }
