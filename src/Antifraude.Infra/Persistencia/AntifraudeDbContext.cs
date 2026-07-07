using System.Text.Json;
using Antifraude.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// DbContext do MySQL (Pomelo). Modela três tabelas: <c>casos</c>, <c>scoring_config</c>
/// e <c>auditoria</c> (append-only, protegida por trigger criado em migration dedicada).
/// </summary>
public sealed class AntifraudeDbContext(DbContextOptions<AntifraudeDbContext> options)
    : DbContext(options)
{
    public DbSet<Caso> Casos => Set<Caso>();

    public DbSet<ScoringConfig> ScoringConfigs => Set<ScoringConfig>();

    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    public DbSet<RegistroIngestao> AuditoriaIngestao => Set<RegistroIngestao>();

    public DbSet<SinistroProcessado> SinistrosProcessados => Set<SinistroProcessado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Caso>(e =>
        {
            e.ToTable("casos");
            e.HasKey(c => c.CaseId);
            e.Property(c => c.CaseId).HasColumnName("case_id");
            e.Property(c => c.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(40);
            e.Property(c => c.Faixa).HasColumnName("faixa").HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Rota).HasColumnName("rota").HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Score).HasColumnName("score");
            e.Property(c => c.VersaoConfig).HasColumnName("versao_config");
            e.Property(c => c.VersaoProvider).HasColumnName("versao_provider").HasMaxLength(60);
            e.Property(c => c.DadosIncompletos).HasColumnName("dados_incompletos");
            e.Property(c => c.PayloadParcial).HasColumnName("payload_parcial");
            e.Property(c => c.CriadoEm).HasColumnName("criado_em");
        });

        modelBuilder.Entity<ScoringConfig>(e =>
        {
            e.ToTable("scoring_config");
            e.HasKey(c => c.Versao);
            e.Property(c => c.Versao).HasColumnName("versao").ValueGeneratedNever();
            e.Property(c => c.Ativa).HasColumnName("ativa");
            e.Property(c => c.LimiarMedio).HasColumnName("limiar_medio");
            e.Property(c => c.LimiarAlto).HasColumnName("limiar_alto");
            e.Property(c => c.CriadaEm).HasColumnName("criada_em");
            // Pesos persistidos como JSON — dicionário de peso por sinal.
            e.Property(c => c.Pesos)
                .HasColumnName("pesos_json")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, double>>(v, (JsonSerializerOptions?)null)
                         ?? new Dictionary<string, double>())
                .HasColumnType("json")
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyDictionary<string, double>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                              == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v));
            // Índice para resolver a versão ativa rapidamente.
            e.HasIndex(c => c.Ativa).HasDatabaseName("ix_scoring_config_ativa");
        });

        modelBuilder.Entity<RegistroAuditoria>(e =>
        {
            e.ToTable("auditoria");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.CaseId).HasColumnName("case_id");
            // Sinais como JSON — a conversão de armazenamento mora no adapter, não no domínio.
            e.Property(a => a.Sinais)
                .HasColumnName("sinais_json")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<Sinal>>(v, (JsonSerializerOptions?)null) ?? new List<Sinal>())
                .HasColumnType("json")
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<Sinal>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                              == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v));
            e.Property(a => a.Score).HasColumnName("score");
            e.Property(a => a.Faixa).HasColumnName("faixa").HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Rota).HasColumnName("rota").HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.VersaoConfig).HasColumnName("versao_config");
            e.Property(a => a.VersaoProvider).HasColumnName("versao_provider").HasMaxLength(60);
            e.Property(a => a.Causa).HasColumnName("causa").HasMaxLength(1000);
            e.Property(a => a.Ator).HasColumnName("ator").HasMaxLength(60);
            e.Property(a => a.PayloadParcial).HasColumnName("payload_parcial");
            e.Property(a => a.CarimbadoEm).HasColumnName("carimbado_em");
            e.HasIndex(a => a.CaseId).HasDatabaseName("ix_auditoria_case_id");
        });

        modelBuilder.Entity<RegistroIngestao>(e =>
        {
            e.ToTable("auditoria_ingestao");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.CaseId).HasColumnName("case_id");
            e.Property(a => a.IdSinistro).HasColumnName("id_sinistro").HasMaxLength(100);
            e.Property(a => a.TemApolice).HasColumnName("tem_apolice");
            e.Property(a => a.TemAparelho).HasColumnName("tem_aparelho");
            e.Property(a => a.TemFotos).HasColumnName("tem_fotos");
            e.Property(a => a.TemMetadados).HasColumnName("tem_metadados");
            e.Property(a => a.PayloadParcial).HasColumnName("payload_parcial");
            e.Property(a => a.Idempotencia).HasColumnName("idempotencia").HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.Destino).HasColumnName("destino").HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.RecebidoEm).HasColumnName("recebido_em");
            e.HasIndex(a => a.CaseId).HasDatabaseName("ix_auditoria_ingestao_case_id");
            e.HasIndex(a => a.IdSinistro).HasDatabaseName("ix_auditoria_ingestao_id_sinistro");
        });

        modelBuilder.Entity<SinistroProcessado>(e =>
        {
            e.ToTable("sinistros_processados");
            e.HasKey(s => s.IdSinistro);
            e.Property(s => s.IdSinistro).HasColumnName("id_sinistro").HasMaxLength(100);
            e.Property(s => s.PrimeiraVezEm).HasColumnName("primeira_vez_em");
            e.HasIndex(s => s.PrimeiraVezEm).HasDatabaseName("ix_sinistros_processados_primeira_vez_em");
        });
    }
}
