using ArchiveAgent.Core.Agents;
using ArchiveAgent.Core.Ai;
using ArchiveAgent.Core.Data;
using ArchiveAgent.Core.Domain;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Data ---
builder.Services.AddDbContext<ArchiveDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// --- Claude / AI ---
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection("Claude"));
builder.Services.AddHttpClient<IClaudeClient, ClaudeClient>();
builder.Services.AddScoped<ClassificationService>();
builder.Services.AddScoped<ArchiveAgentService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Scaffold convenience: ensure DB exists + seed. In production use EF migrations instead.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
    db.Database.EnsureCreated();
    DbSeeder.Seed(db);
}

app.UseSwagger();
app.UseSwaggerUI();

// Run one batch of the agent loop.
app.MapPost("/pipeline/run", async (ArchiveAgentService agent, int? batchSize, CancellationToken ct) =>
    Results.Ok(await agent.RunAsync(batchSize ?? 25, ct)));

app.MapGet("/records", async (ArchiveDbContext db, RecordStatus? status) =>
    await db.Records
        .Where(r => status == null || r.Status == status)
        .OrderBy(r => r.Id).Take(100).ToListAsync());

app.MapGet("/archive", async (ArchiveDbContext db) =>
    await db.ArchivedRecords.OrderByDescending(a => a.ArchivedUtc).Take(100).ToListAsync());

app.MapGet("/audit", async (ArchiveDbContext db) =>
    await db.AuditLogs.OrderByDescending(a => a.Id).Take(100).ToListAsync());

app.MapGet("/review-queue", async (ArchiveDbContext db) =>
    await db.ReviewItems.Where(x => !x.Resolved).OrderBy(x => x.Id).ToListAsync());

app.Run();
