using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RavenDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Raven") ?? "Data Source=raven.db"));
builder.Services.AddHealthChecks().AddDbContextCheck<RavenDbContext>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapHealthChecks("/health");
app.MapGet("/api", () => Results.Ok(new { name = "RAVEN API", status = "initialized" }));

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
