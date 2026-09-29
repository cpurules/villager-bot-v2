using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VillagerBot.Data;

/// <summary>
/// Used by <c>dotnet ef</c> to build the model. Generating migrations never connects, so a placeholder connection
/// string is fine; set <c>ConnectionStrings__VillagerBot</c> to run <c>dotnet ef database update</c> against a real server.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VillagerBotDbContext>
{
    public VillagerBotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__VillagerBot")
            ?? "Host=localhost;Database=villager_bot;Username=villager_bot";

        var builder = new DbContextOptionsBuilder<VillagerBotDbContext>();
        VillagerBotDbContext.Configure(builder, connectionString);
        return new VillagerBotDbContext(builder.Options);
    }
}
