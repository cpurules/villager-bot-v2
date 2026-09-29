using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using VillagerBot.Bot;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("VillagerBot")
    ?? throw new InvalidOperationException("Connection string 'VillagerBot' is not configured.");

builder.Services
    .AddOptions<VillagerBotOptions>()
    .Bind(builder.Configuration.GetSection(VillagerBotOptions.Section));

builder.Services
    .AddDbContext<VillagerBotDbContext>(options => VillagerBotDbContext.Configure(options, connectionString))
    .AddSingleton(VillagerCatalog.LoadEmbedded());

// Interactions need no intents; Guilds (non-privileged) keeps the guild, channel and role cache populated.
// Never add MessageContent or other privileged intents (see CLAUDE.md).
builder.Services
    .AddDiscordGateway(options => options.Intents = GatewayIntents.Guilds)
    .AddApplicationCommands();

var host = builder.Build();

host.AddModules(typeof(Program).Assembly);

if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VillagerBotDbContext>().Database.MigrateAsync();
}

await host.RunAsync();
