using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RavenDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Raven") ?? "Data Source=raven.db"));
builder.Services.AddHealthChecks().AddDbContextCheck<RavenDbContext>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];

    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.Configure<Crawl4AiLocalOptions>(
    builder.Configuration.GetSection(Crawl4AiLocalOptions.SectionName));
builder.Services.AddHttpClient<ICrawlerStatusProbe, Crawl4AiLocalStatusProbe>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Crawl4AiLocalOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(3);
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("DevelopmentFrontend");
app.MapHealthChecks("/health");
app.MapGet("/api", () => Results.Ok(new { name = "RAVEN API", status = "initialized" }));
app.MapCompanyEndpoints();
app.MapSystemEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program;
