using Antifraude.Api.Contratos;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Config por env var, logs JSON com scopes (correlação por caseId) e composição da infra.
builder.AddAntifraudeHostDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Higiene do store de deduplicação: purga entradas expiradas (> 24h) periodicamente.
builder.Services.AddHostedService<PurgaDedupService>();

var app = builder.Build();

// Migrations + filas aplicadas no start, ANTES de aceitar tráfego / ficar healthy.
await InicializarAsync(app);

app.UseSwagger();
app.UseSwaggerUI();

// GET /health — liveness/readiness. Só responde 200 depois do InicializarAsync acima.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

// POST /sinistros — ingestão real (Feature 2.1): valida formato mínimo, aplica idempotência,
// enfileira e audita. NUNCA decide o mérito. Só corpo ilegível vira 400; evento sem
// idSinistro vira 202 + fila de erro técnico (o sinistro já existe no sistema principal).
app.MapPost("/sinistros", async (
    SinistroRequest request,
    ISinistroQueue queue,
    ISinistroDedupStore dedup,
    IAuditLogIngestao auditoria,
    ILoggerFactory loggerFactory,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Sinistros");

    if (request is null)
    {
        // Corpo ausente/ilegível é a ÚNICA rejeição de formato — nunca decisão de fraude.
        return Results.BadRequest(new { erros = new[] { "Corpo da requisição ausente ou inválido." } });
    }

    var caseId = Guid.NewGuid();
    using (logger.BeginScope(new Dictionary<string, object> { ["caseId"] = caseId }))
    {
        var aceito = Results.Accepted($"/sinistros/{caseId}", new { caseId });

        // Evento não-processável: sem idSinistro não há como identificar/deduplicar o caso.
        if (!request.TemIdSinistro)
        {
            if (!await TentarErroTecnicoAsync(queue, request, "sem idSinistro", logger, ct))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            await AuditarAsync(auditoria, caseId, request, ResultadoIdempotencia.PrimeiraVez, DestinoRoteamento.FilaErroTecnico, clock, logger, ct);
            logger.LogWarning("Evento sem idSinistro roteado para a fila de erro técnico.");
            return aceito;
        }

        // Idempotência (fail-open): se o store cair, processa mesmo assim + alerta.
        ResultadoIdempotencia idem;
        try
        {
            idem = await dedup.RegistrarSeNovoAsync(request.IdSinistro!, ct)
                ? ResultadoIdempotencia.PrimeiraVez
                : ResultadoIdempotencia.DuplicadoDescartado;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Store de deduplicação indisponível — seguindo (fail-open) e registrando p/ reconciliação.");
            idem = ResultadoIdempotencia.ChecagemIndisponivel;
        }

        if (idem == ResultadoIdempotencia.DuplicadoDescartado)
        {
            await AuditarAsync(auditoria, caseId, request, idem, DestinoRoteamento.Descartado, clock, logger, ct);
            logger.LogInformation("Evento duplicado descartado (idempotência). idSinistro={IdSinistro}", request.IdSinistro);
            return aceito;
        }

        // Enfileira na fila principal (com retry/backoff interno). Em falha persistente, escala.
        var destino = DestinoRoteamento.FilaProcessamento;
        var sinistro = request.ParaDominio(caseId);
        try
        {
            await queue.PublishAsync(sinistro, ct);
        }
        catch (EnfileiramentoException ex)
        {
            logger.LogError(ex, "Enfileiramento esgotou o retry; escalando para a fila de erro técnico.");
            destino = DestinoRoteamento.FilaErroTecnico;
            if (!await TentarErroTecnicoAsync(queue, sinistro, "enfileiramento falhou após retries", logger, ct))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        await AuditarAsync(auditoria, caseId, request, idem, destino, clock, logger, ct);
        logger.LogInformation("Sinistro aceito. destino={Destino} payloadParcial={Parcial}", destino, request.PayloadParcial);
        return aceito;
    }
})
.WithName("ReceberSinistro");

app.Run();

// Publica na fila de erro técnico; retorna false se nem isso for possível (broker fora).
static async Task<bool> TentarErroTecnicoAsync(
    ISinistroQueue queue, object corpo, string motivo, ILogger logger, CancellationToken ct)
{
    try
    {
        await queue.PublishErroTecnicoAsync(corpo, motivo, ct);
        return true;
    }
    catch (Exception ex)
    {
        // Indisponibilidade total do broker: única situação em que sinalizamos o produtor (503).
        logger.LogCritical(ex, "Broker indisponível: falha ao rotear para a fila de erro técnico. motivo={Motivo}", motivo);
        return false;
    }
}

// Auditoria de ingestão (best-effort): falha aqui é logada, não derruba o evento já roteado.
static async Task AuditarAsync(
    IAuditLogIngestao auditoria,
    Guid caseId,
    SinistroRequest request,
    ResultadoIdempotencia idem,
    DestinoRoteamento destino,
    TimeProvider clock,
    ILogger logger,
    CancellationToken ct)
{
    try
    {
        await auditoria.RegistrarAsync(new RegistroIngestao
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            IdSinistro = request.TemIdSinistro ? request.IdSinistro : null,
            TemApolice = request.TemApolice,
            TemAparelho = request.TemAparelho,
            TemFotos = request.TemFotos,
            TemMetadados = request.TemMetadados,
            PayloadParcial = request.PayloadParcial,
            Idempotencia = idem,
            Destino = destino,
            RecebidoEm = clock.GetUtcNow(),
        }, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao registrar auditoria de ingestão. caseId={CaseId}", caseId);
    }
}

static async Task InicializarAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

    var db = sp.GetRequiredService<AntifraudeDbContext>();
    logger.LogInformation("Aplicando migrations...");
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, logger);

    logger.LogInformation("Garantindo filas SQS...");
    var queue = sp.GetRequiredService<ISinistroQueue>();
    await queue.EnsureQueueAsync();

    logger.LogInformation("Bootstrap concluído — API pronta.");
}

// Exposto para os testes de integração (WebApplicationFactory).
public partial class Program;
