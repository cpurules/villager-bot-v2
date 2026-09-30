using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Panel;
using VillagerBot.Bot.Relays;
using VillagerBot.Bot.Requests;
using VillagerBot.Bot.Staff;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

// Read appsettings from the app's own folder, whatever directory it's launched from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// DOTNET_ENVIRONMENT picks the server: appsettings.Production.json (live) or appsettings.Test.json (test server).
// User secrets hold the token and connection string for local runs in any environment.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var connectionString = builder.Configuration.GetConnectionString("VillagerBot")
    ?? throw new InvalidOperationException("Connection string 'VillagerBot' is not configured.");

builder.Services
    .AddSingleton<IValidateOptions<VillagerBotOptions>, VillagerBotOptionsValidator>()
    .AddOptions<VillagerBotOptions>()
    .Bind(builder.Configuration.GetSection(VillagerBotOptions.Section))
    .ValidateOnStart();

builder.Services
    .AddDbContext<VillagerBotDbContext>(options => VillagerBotDbContext.Configure(options, connectionString))
    .AddSingleton(TimeProvider.System)
    .AddSingleton(VillagerCatalog.LoadEmbedded())
    .AddSingleton<MemberAccess>()
    .AddSingleton<RequestViews>()
    .AddScoped<RequestService>()
    .AddScoped<RequestFlow>()
    .AddScoped<RelayService>()
    .AddScoped<PanelService>()
    .AddSingleton<VillagerGroups>()
    .AddSingleton<StaffViews>()
    .AddSingleton<ChannelCreationLock>()
    .AddScoped<CategoryManager>()
    .AddScoped<PullService>()
    .AddScoped<PullReporter>()
    .AddScoped<CloseService>()
    .AddScoped<LookupService>()
    .AddHostedService<StartupCheck>()
    .AddHostedService<DepartedMemberCleanup>();

// Interactions need no intents; Guilds (non-privileged) keeps the guild, channel and role cache populated.
// Never add MessageContent or other privileged intents (see CLAUDE.md).
// Failed preconditions and errors are reported privately to the user who clicked or typed.
builder.Services
    .AddDiscordGateway((options, services) =>
    {
        options.Intents = GatewayIntents.Guilds;

        var activity = services.GetRequiredService<IOptions<VillagerBotOptions>>().Value.Activity;
        options.Presence = new PresenceProperties(UserStatusType.Online)
        {
            Activities = string.IsNullOrWhiteSpace(activity.Text)
                ? []
                // Custom statuses show their text from State; the other types show Name.
                : [new UserActivityProperties(activity.Text, activity.Type) { State = activity.Type == UserActivityType.Custom ? activity.Text : null }],
        };
    })
    .AddApplicationCommands(options => options.ResultHandler = ApplicationCommandResultHandler<ApplicationCommandContext>.Ephemeral)
    .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>(options =>
        options.ResultHandler = ComponentInteractionResultHandler<ButtonInteractionContext>.Ephemeral)
    .AddComponentInteractions<StringMenuInteraction, StringMenuInteractionContext>(options =>
        options.ResultHandler = ComponentInteractionResultHandler<StringMenuInteractionContext>.Ephemeral)
    .AddComponentInteractions<ModalInteraction, ModalInteractionContext>(options =>
        options.ResultHandler = ComponentInteractionResultHandler<ModalInteractionContext>.Ephemeral);

var host = builder.Build();

host.AddModules(typeof(Program).Assembly);

if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VillagerBotDbContext>().Database.MigrateAsync();
}

await host.RunAsync();
