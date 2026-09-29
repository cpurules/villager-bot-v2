using Microsoft.Extensions.Options;

namespace VillagerBot.Bot.Configuration;

/// <summary>Fails startup with a list of every missing Discord ID, rather than failing later mid-interaction.</summary>
public sealed class VillagerBotOptionsValidator : IValidateOptions<VillagerBotOptions>
{
    public ValidateOptionsResult Validate(string? name, VillagerBotOptions options)
    {
        var missing = new List<string>();

        void Require(string key, ulong value)
        {
            if (value == 0)
                missing.Add($"{VillagerBotOptions.Section}:{key}");
        }

        Require("GuildId", options.GuildId);
        Require("Roles:Villagers", options.Roles.Villagers);
        Require("Roles:HavenHunter", options.Roles.HavenHunter);
        Require("Roles:Moderator", options.Roles.Moderator);
        Require("Roles:Haven", options.Roles.Haven);
        Require("Roles:HunterHiatus", options.Roles.HunterHiatus);
        Require("Channels:Info", options.Channels.Info);
        Require("Channels:Requests", options.Channels.Requests);
        Require("Channels:ModMail", options.Channels.ModMail);
        Require("Channels:StarterKits", options.Channels.StarterKits);
        Require("Channels:StaffLog", options.Channels.StaffLog);
        Require("Channels:TimeoutLog", options.Channels.TimeoutLog);
        Require("Categories:Main", options.Categories.Main);

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Missing Discord IDs for this environment (set them in appsettings.{{Environment}}.json or environment variables): {string.Join(", ", missing)}");
    }
}
