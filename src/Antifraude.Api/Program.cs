using Antifraude.Api.Contratos;
using Antifraude.Infra;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Config por env var, logs JSON com scopes (correlação por caseId) e composição da infra.
builder.AddAntifraudeHostDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Migrations + fila aplicadas no start, ANTES de aceitar tráfego / ficar healthy.
await InicializarAsync(app);

app.UseSwagger();
app.UseSwaggerUI();

// GET /health — liveness/readiness. Só responde 200 depois do InicializarAsync acima.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

// POST /sinistros — valida na borda, enfileira e responde 202. NUNCA decide o mérito.
app.MapPost("/sinistros", async (
    SinistroRequest request,
    ISinistroQueue queue,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Sinistros");

    if (request is null)
    {
        return Results.BadRequest(new { erros = new[] { "Corpo da requisição ausente ou inválido." } });
    }

    var erros = request.Validar();
    if (erros.Count > 0)
    {
        // 400 é sobre FORMATO — nunca uma decisão de fraude.
        return Results.BadRequest(new { erros });
    }

    var caseId = Guid.NewGuid();
    using (logger.BeginScope(new Dictionary<string, object> { ["caseId"] = caseId }))
    {
        var sinistro = request.ParaDominio(caseId);
        await queue.PublishAsync(sinistro, ct);
        logger.LogInformation("Sinistro aceito e enfileirado.");
    }

    // 202: aceito para processamento assíncrono. Sem veredito.
    return Results.Accepted($"/sinistros/{caseId}", new { caseId });
})
.WithName("ReceberSinistro");

app.Run();

static async Task InicializarAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

    var db = sp.GetRequiredService<AntifraudeDbContext>();
    logger.LogInformation("Aplicando migrations...");
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, logger);

    logger.LogInformation("Garantindo fila SQS...");
    var queue = sp.GetRequiredService<ISinistroQueue>();
    await queue.EnsureQueueAsync();

    logger.LogInformation("Bootstrap concluído — API pronta.");
}

// Exposto para os testes de integração (WebApplicationFactory).
public partial class Program;
