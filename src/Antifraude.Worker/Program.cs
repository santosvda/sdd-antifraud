using Antifraude.Infra;
using Antifraude.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddAntifraudeHostDefaults();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
