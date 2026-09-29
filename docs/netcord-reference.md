# NetCord Reference & Research Guide

Working reference for building the Villager Bot rewrite on NetCord. It summarizes the official
guides at <https://netcord.dev/guides/> and explains how to research the API docs at
<https://netcord.dev/docs/>.

> **Rule:** never guess a NetCord type, method, overload, or property. NetCord is pre-1.0 and its
> API changes between betas. Before using an API you haven't verified in this session, look it up
> with one of the methods in [§2](#2-researching-the-api). If the docs and this file disagree,
> **the docs win**. Update this file when you find a discrepancy.

Last researched: 2026-09-28, against NetCord `1.0.0-beta.27`.

---

## 1. Version & platform facts

| Fact | Detail |
|---|---|
| **No stable release exists** | Every NetCord package on NuGet is a prerelease. The latest is `1.0.0-beta.27` (2026-09-26). There is no non-prerelease version to choose, so "stable" in practice means **pin one exact beta** and upgrade deliberately. |
| Release cadence | Very frequent: beta.22 to beta.27 shipped within about a week. Pin exact versions in the `.csproj` (no floating `*` versions). Upgrade one beta at a time and re-check any API you touch. |
| Target framework | **.NET 10 or higher only** (`net10.0`). The guide states older versions are unsupported, and the NuGet packages ship only a `net10.0` target. .NET 10 is an LTS release. |
| Install | `dotnet add package NetCord --prerelease` (or with `--version 1.0.0-beta.27` to pin). |
| Source for a version | Every API page links to the source file at the tagged version, e.g. `https://github.com/NetCordDev/NetCord/blob/1.0.0-beta.27/...`. |

### Packages

| Package | Purpose | Needed for this bot? |
|---|---|---|
| `NetCord` | Core: `GatewayClient`, `RestClient`, entities, models | Yes |
| `NetCord.Services` | Application commands, component interactions, text commands, preconditions, type readers | Yes |
| `NetCord.Hosting` | .NET Generic Host integration (`AddDiscordGateway`, `AddDiscordRest`, gateway handlers) | Yes (recommended approach) |
| `NetCord.Hosting.Services` | Generic Host integration for `NetCord.Services` (`AddApplicationCommands`, `AddComponentInteractions`, `AddModules`) | Yes |
| `NetCord.Hosting.AspNetCore` | Receive interactions over **HTTP** (`UseHttpInteractions`). Only needed for a serverless or HTTP-interactions deployment. | Only if HTTP-hosted |
| `Microsoft.Extensions.Hosting` | Generic Host itself | Yes |

---

## 2. Researching the API

The API docs are a DocFX site with 1,488 types across 33 namespaces. There are four ways to look
things up, fastest first.

### 2.1 `xrefmap.yml`: the master index (best for "does X exist / what's its signature")

`https://netcord.dev/xrefmap.yml` (about 9 MB) lists **every** namespace, type, member, and overload
with its docs URL. Each entry looks like:

```yaml
- uid: NetCord.Rest.RestClient.CreateGuildChannelAsync(System.UInt64,NetCord.Rest.GuildChannelProperties,NetCord.Rest.RestRequestProperties,System.Threading.CancellationToken)
  name: CreateGuildChannelAsync(ulong, GuildChannelProperties, RestRequestProperties?, CancellationToken)
  href: docs/NetCord.Rest.RestClient.html#NetCord_Rest_RestClient_CreateGuildChannelAsync_System_UInt64_...
  commentId: M:NetCord.Rest.RestClient.CreateGuildChannelAsync(...)
```

- `commentId` prefix gives the kind: `N:` namespace, `T:` type, `M:` method or constructor, `P:` property,
  `F:` field or enum member, `E:` event, `Overload:` an overload group (uid ends in `*`).
- The `uid` contains the **full parameter type list**, so it answers "what overloads exist?" directly.
- Generic types use a backtick arity: ``ApplicationCommandModule`1``, ``ApplicationCommandServiceOptions`2``.
  Generic methods use a double backtick: ``AddApplicationCommands``2(...)``.

Workflow: download once per session to the scratchpad, then grep it. For example:

```bash
curl -s https://netcord.dev/xrefmap.yml -o xrefmap.yml
grep -o "uid: NetCord.Rest.RestClient.GetGuildUserAsync([^ ]*" xrefmap.yml
# list all members of a type:
grep -o "uid: NetCord.Rest.GuildChannelProperties\.[A-Za-z]*" xrefmap.yml | sort -u
```

### 2.2 Type pages (best for docs text, inheritance, inherited members)

URL pattern: `https://netcord.dev/docs/<Full.Type.Name>.html`

- Generic arity becomes a dash: ``ApplicationCommandService`1`` → `NetCord.Services.ApplicationCommands.ApplicationCommandService-1.html`.
- Member anchors replace `.`, `(`, `)`, and `,` with `_`, e.g.
  `NetCord.Rest.RestClient.html#NetCord_Rest_RestClient_SendMessageAsync_`.
- `memberLayout` is `SamePage`: all members of a type appear on the type's page. Pages for large
  types such as `RestClient` are very long, so use the anchor or xrefmap instead of reading the whole page.
- Each page shows: summary, C# declaration, **Inheritance** chain, **Implements**, **Inherited Members**
  (important: many handy members live on base types), then Constructors, Properties, Methods,
  Events, and a link to the source on GitHub at the current tag.
- Namespace pages (`docs/NetCord.Rest.html`) list every type in the namespace with a one-line summary.

### 2.3 Search and TOC indexes

- `https://netcord.dev/index.json` (about 3 MB): full-text search index keyed by page, with `title` and `summary`. Useful for
  "which type is about X?" questions.
- `https://netcord.dev/docs/toc.json`: namespace → type tree.
- `https://netcord.dev/guides/toc.json`: guide tree (see §3).

### 2.4 Source on GitHub

When the docs are thin (many members have no XML summary), read the source at the pinned tag via the
"source" link on the type page. The repo is `NetCordDev/NetCord`.

### 2.5 Fetching tips

- WebFetch summarizes pages and can drop detail. For exact signatures or code, prefer `curl` and
  grep, or parse the `<article>` element of the HTML.
- Guide pages use tabbed code blocks: **".NET Generic Host" vs "Bare Bones"** and **"Classic Syntax"
  (object initializers) vs "Fluent Syntax" (`.WithX()` / `.AddX()`)**. Both syntaxes are equivalent;
  every `*Properties` type exposes settable properties plus `WithX`/`AddX` fluent methods.

### Key namespaces for this project

| Namespace | What lives there |
|---|---|
| `NetCord` | Entities: `User`, `GuildUser`, `Guild`, `TextChannel`, `Interaction` types, `Permissions`, `Color`, `MessageFlags`, `ChannelType`, `PermissionOverwriteType` |
| `NetCord.Gateway` | `GatewayClient`, `GatewayIntents`, gateway `Message`, event args |
| `NetCord.Rest` | `RestClient`, all `*Properties` builders (messages, embeds, components, channels, permission overwrites, attachments), `InteractionCallback` |
| `NetCord.Services` | Preconditions (`RequireUserPermissions`, `RequireContext`, `PreconditionAttribute<T>`), `IFailResult`, `UserId` |
| `NetCord.Services.ApplicationCommands` | `ApplicationCommandModule<T>`, `[SlashCommand]`, `[SubSlashCommand]`, `[SlashCommandParameter]`, contexts, `IAutocompleteProvider<T>` |
| `NetCord.Services.ComponentInteractions` | `ComponentInteractionModule<T>`, `[ComponentInteraction]`, button, menu, and modal contexts |
| `NetCord.Hosting.Gateway` | `AddDiscordGateway`, `GatewayClientOptions`, `I*GatewayHandler` event interfaces, `AddGatewayHandlers` |
| `NetCord.Hosting.Services.ApplicationCommands` | `AddApplicationCommands`, `ApplicationCommandServiceOptions`, `ApplicationCommandResultHandler<T>` |
| `NetCord.Hosting.Services.ComponentInteractions` | `AddComponentInteractions<TInteraction, TContext>` |
| `NetCord.Hosting.Services` | `AddModules(IHost, Assembly)` |
| `NetCord.Hosting.AspNetCore` | `UseHttpInteractions` (HTTP deployments only) |

---

## 3. Guide map

All at `https://netcord.dev/guides/…`. Relevance to this project is noted.

| Section | Page | Relevance |
|---|---|---|
| Getting Started | `getting-started/installation.html`, `getting-started/making-a-bot.html` | High |
| Events | `events/intents.html`, `events/first-events.html` | Medium: we need few or no events |
| Basic Concepts | `basic-concepts/responding-to-interactions.html` | **Critical** |
| | `basic-concepts/sending-messages.html` | **Critical** |
| | `basic-concepts/http-interactions.html` | High if hosting serverless |
| | `basic-concepts/sharding.html` | None (one guild) |
| | `installing-native-dependencies.html` (hidden, not in TOC) | Needed for HTTP interactions (libsodium) |
| Voice | `voice/*` | None |
| Services | `services/application-commands/{introduction,parameters,subcommands,permissions,localizations,multiple-services}.html` | **Critical** |
| | `services/component-interactions/{introduction,parameters}.html` | **Critical** (buttons replace reaction menus) |
| | `services/text-commands/*` | **Do not use.** Text/prefix commands need the Message Content intent, which this bot no longer has. |
| | `services/dependency-injection.html`, `preconditions.html`, `type-readers.html`, `custom-module-bases-and-contexts.html` | High |

---

## 4. Core patterns (verified against the guides)

### 4.1 Generic Host bootstrap (gateway bot)

```csharp
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(options =>
    {
        options.Intents = GatewayIntents.Guilds; // see §4.8
    })
    .AddApplicationCommands()
    .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>()
    .AddComponentInteractions<StringMenuInteraction, StringMenuInteractionContext>()
    .AddComponentInteractions<ModalInteraction, ModalInteractionContext>();

var host = builder.Build();
host.AddModules(typeof(Program).Assembly); // discovers command + component modules
await host.RunAsync();
```

Configuration comes from `appsettings.json`, environment variables, or user secrets under the `Discord` section:

```json
{ "Discord": { "Token": "…", "PublicKey": "… (HTTP interactions only)" } }
```

`GatewayClientOptions` properties (verified): `Token`, `Intents`, `Presence`, `CacheProvider`,
`Compression`, `ReconnectStrategy`, `LatencyTimer`, `LargeThreshold`, `Shard`,
`RestClientConfiguration`, `DefaultMessageProperties`, `AutoStartStop`, `PublicKey`, and others.

### 4.2 Application command modules

```csharp
using NetCord.Services.ApplicationCommands;

public class ExampleModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("pong", "Pong!")]
    public static string Pong() => "Ping!";
}
```

- **Slash command names must be lowercase.** Up to 25 parameters per command.
- Return types: `string` or `InteractionMessageProperties` become the response automatically
  (`Task<…>` / `ValueTask<…>` also work). You can also call the module helpers yourself:
  `RespondAsync(InteractionCallbackProperties, …)`, `ModifyResponseAsync(Action<MessageOptions>, …)`,
  `FollowupAsync(InteractionMessageProperties, …)`, `GetResponseAsync`, `DeleteResponseAsync`,
  `GetFollowupAsync`, `ModifyFollowupAsync`, `DeleteFollowupAsync`. These are verified members of
  ``ApplicationCommandModule`1``.
- `Context` (via ``BaseApplicationCommandModule`1``) exposes `Interaction`, `User`, `Guild`,
  `Channel`, and `Client`. For HTTP hosting the context type is `HttpApplicationCommandContext` instead of
  `ApplicationCommandContext`.
- **Subcommands:** put `[SlashCommand("group", "…")]` on the class and `[SubSlashCommand("name", "…")]` on
  methods. Nested classes create subcommand groups. Only slash commands support subcommands.
- `[SlashCommand]` / `ApplicationCommandAttribute` properties (verified): `Name`,
  `DefaultGuildPermissions`, `Contexts` (`InteractionContextType.Guild` / `BotDMChannel` / …),
  `IntegrationTypes`, `Nsfw`, `Register`.
- Minimal-API alternative: `host.AddSlashCommand("ping", "Ping!", () => "Pong!")`. In a delegate,
  parameters *before* the context parameter are DI services and parameters *after* it are command options.

### 4.3 Parameters

- Optional parameter: give it a default value (`User? user = null`).
- Rename or describe: `[SlashCommandParameter(Name = "base", Description = "…")]`. Verified properties:
  `Name`, `Description`, `MinValue`, `MaxValue` (numeric), `MinLength`, `MaxLength` (text),
  `ChoicesProviderType`, `AutocompleteProviderType`, `TypeReaderType`, `AllowedChannelTypes`, `FileTypes`.
- **Enums become choices automatically.** Rename a choice with `[SlashCommandChoice(Name = "Guinea Pig")]`.
  Discord caps choices at 25, so the ~410 villagers **must use autocomplete**, not an enum.
- Supported entity parameters include `User`, `GuildUser`, channels, roles, and attachments. User commands take a
  single `User` parameter and message commands a single `RestMessage` parameter.

### 4.4 Autocomplete (use for villager name inputs)

```csharp
public class VillagerAutocomplete(IVillagerCatalog catalog)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var input = option.Value ?? "";
        var result = catalog.Search(input).Take(25)
            .Select(v => new ApplicationCommandOptionChoiceProperties(v.Name, v.Key));
        return new(result);
    }
}

// usage
[SlashCommand("request", "Request a villager")]
public string Request(
    [SlashCommandParameter(AutocompleteProviderType = typeof(VillagerAutocomplete))] string villager) => …;
```

Autocomplete providers get constructor DI and behave like transient services. Autocomplete is a
suggestion only, so **always re-validate the submitted value**. The hosted service also needs an autocomplete
context registered. Check the `AddApplicationCommands` generic overloads (``AddApplicationCommands``3``)
before wiring this up.

### 4.5 Responding to interactions

`InteractionCallback` factory (verified in the guide):

| Callback | Use |
|---|---|
| `InteractionCallback.Message(InteractionMessageProperties)` | Normal reply |
| `InteractionCallback.DeferredMessage(MessageFlags.Ephemeral?)` | "Thinking…". Up to **15 minutes** to finish via `ModifyResponseAsync` / `SendFollowupMessageAsync`. |
| `InteractionCallback.DeferredModifyMessage` | Components: acknowledge without a loading state |
| `InteractionCallback.ModifyMessage(m => …)` | Components: edit the message the button is on |
| `InteractionCallback.Modal(ModalProperties)` | Pop a form. Components are wrapped in `LabelProperties("Label", new TextInputProperties(id, TextInputStyle.Short))`. |
| `InteractionCallback.Autocomplete([...])` | Autocomplete results |
| `InteractionCallback.Pong` | HTTP ping (handled automatically by `NetCord.Hosting.AspNetCore`) |

- Discord requires the **initial response within 3 seconds**. Defer first for any command that calls
  the database and then does several REST calls (e.g. channel creation in `/select`).
- **Ephemeral** replies (visible only to the invoker) suit staff commands whose output shouldn't clutter a channel:
  `MessageFlags.Ephemeral` on the message or deferral. (Member commands run in the bot's DMs, where this matters less.) To make *all* failures and results ephemeral by default:
  `.AddApplicationCommands(o => o.ResultHandler = ApplicationCommandResultHandler<ApplicationCommandContext>.Ephemeral)`
  (`Default` and `Ephemeral` are verified static members). This pattern appears in the voice guide sample.

### 4.6 Messages, embeds, components

- Property types: `MessageProperties` (REST `SendMessageAsync`), `InteractionMessageProperties`
  (interaction responses), `ReplyMessageProperties`, `WebhookMessageProperties`. All implement `IMessageProperties`
  (`Content`, `Embeds`, `Components`, `Attachments`, `AllowedMentions`, `Flags`). Strings convert
  implicitly to message properties.
- **Embeds:** `EmbedProperties { Title, Description, Url, Timestamp = DateTimeOffset.UtcNow, Color = new(0x40E0D0), Footer = new() { Text }, Thumbnail = "url", Image, Author, Fields = [ new() { Name, Value, Inline } ] }`. Up to 10 per message.
- **Allowed mentions:** `AllowedMentionsProperties.All` / `.None` / custom `{ AllowedUsers = [...], AllowedRoles = [...] }`.
  The old bot pings users in text (`<@id>`). Set allowed mentions explicitly where a ping is intended.
- **Attachments:** `new AttachmentProperties("file.png", stream)`. Up to 10.
- **Buttons:** `new ActionRowProperties { new ButtonProperties("customId", "Label", ButtonStyle.Success), new ButtonProperties("id2", EmojiProperties.Custom(725149894116900864), ButtonStyle.Secondary) }`.
  Up to 5 buttons per row. `LinkButtonProperties(url, label)` opens a URL (use it for the fandom villager list).
- **Select menus:** `StringMenuProperties("id") { new("Label", "value") { Description, Emoji, Default } }`,
  up to 25 options. Also `UserMenuProperties`, `RoleMenuProperties`, `ChannelMenuProperties`, `MentionableMenuProperties`.
  Common settings: `Placeholder`, `MinValues`, `MaxValues`, `Disabled`.
- Message flags: `MessageFlags.Ephemeral`, `SuppressEmbeds`, `SuppressNotifications`.

### 4.6a Modern components (verified present in beta.27)

- **Modal contents:** each modal component is wrapped in `LabelProperties(string label, ILabelComponentProperties component)`
  (optional `Description`). Types implementing `ILabelComponentProperties`: `TextInputProperties`,
  `StringMenuProperties`, `UserMenuProperties`, `RoleMenuProperties`, `ChannelMenuProperties`,
  `MentionableMenuProperties`, `CheckboxProperties`, `CheckboxGroupProperties`, `RadioGroupProperties`,
  `FileUploadProperties`. So **user pickers and selects can go inside modals**.
- **Components V2 layout types:** `ComponentContainerProperties`, `ComponentSectionProperties`,
  `TextDisplayProperties`, `ComponentSeparatorProperties`, `MediaGalleryProperties`. These are an alternative to
  embeds for rich layouts with inline buttons. Check the Discord rules for the V2 message flag before mixing them with
  `Content`/`Embeds`, and verify the exact flag name in `MessageFlags`.
- **Threads:** `RestClient.CreateGuildThreadAsync(ulong channelId, GuildThreadProperties)` and
  `AddGuildThreadUserAsync(ulong threadId, ulong userId)` exist (a possible alternative to per-request channels).
- **Errors:** REST failures throw `NetCord.Rest.RestException` with `StatusCode` (`HttpStatusCode`), `ReasonPhrase`,
  and `Error`. Use it to detect 404 (member left) or 403 (DMs closed).
- **Privileged-intent trap:** `RestClient.GetGuildUsersAsync` (list all members) needs the privileged Server Members
  intent. Single lookups via `GetGuildUserAsync` don't.

### 4.7 Component interaction handlers (replaces JDA-Utilities `ButtonMenu` / `EventWaiter`)

```csharp
public class ConfirmModule : ComponentInteractionModule<ButtonInteractionContext>
{
    // custom id "leave-confirm:123456789" → userId = 123456789
    [ComponentInteraction("leave-confirm")]
    public async Task<InteractionCallbackProperties> LeaveConfirm(ulong userId) { … }
}
```

- The custom ID format is `name:param1:param2`. The separator `:` is configurable via `ParameterSeparator`.
  **The last parameter is a remainder** (it may contain `:`). Optional params get default values and use empty
  segments (`id:123:`). `params T[]` is supported. Built-in type readers cover primitives,
  `ulong`, enums, `DateTimeOffset`, `TimeSpan`, `GuildUser`, `UserId`, and more.
- **Components have no built-in timeout.** The old 30-second confirmation window must be
  enforced by encoding a timestamp in the custom ID, or by ignoring stale clicks after checking current
  state. Always validate that the clicker is the intended user (`Context.User.Id`).
- Register one service per interaction type: `AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>()`,
  and likewise for `StringMenuInteraction` and `ModalInteraction` (with the matching context).
- Modal handler: `Context.Components.OfType<Label>().Select(l => l.Component).OfType<TextInput>()`.
  `ComponentInteractionModule<T>` has `RespondAsync`, `ModifyResponseAsync`, and `FollowupAsync`.

### 4.8 Intents

- Intents subscribe you to gateway events. **Privileged** intents (`GuildUsers`, `GuildPresences`,
  `MessageContent`) must also be enabled in the Developer Portal.
- **This bot must not use `GatewayIntents.MessageContent`.** Without it, message `Content`, `Embeds`,
  `Attachments`, `Components`, and `Poll` are empty in events. Per Discord policy this also applies to REST
  reads of other users' messages, except messages that mention the bot, DMs with the bot, and the bot's own messages.
- Interactions (slash commands, buttons) work with **no intents at all**. The command guide examples use `Intents = default`.
- Intents members (verified): `Guilds`, `GuildUsers`, `GuildModeration`, `GuildMessages`,
  `GuildMessageReactions`, `DirectMessages`, `MessageContent`, `AllNonPrivileged`, `All`, and others.
- `GuildUsers` (privileged, formerly "Server Members") would populate the member cache as the old bot's
  `GUILD_MEMBERS` + `ChunkingFilter.ALL` did. Consider REST lookups (`GetGuildUserAsync`) instead of
  requesting a privileged intent.

### 4.9 Event handlers (if any are needed)

With the Generic Host, implement `I<Event>GatewayHandler` (e.g. `IMessageCreateGatewayHandler`,
`IGuildUserRemoveGatewayHandler`) and register them with `.AddGatewayHandlers(typeof(Program).Assembly)`.
The bare-bones equivalent is `client.MessageCreate += …`. Handler shape: `public ValueTask HandleAsync(Message message)`.

### 4.10 Preconditions (role gating)

Built-in: `RequireUserPermissions<TContext>`, `RequireBotPermissions<TContext>`,
`RequireContext<TContext>(RequiredContext.Guild)`, and `RequireNsfw<TContext>`. The old bot gates by **role
ID**, so write a custom attribute:

```csharp
public class RequireAnyRoleAttribute<TContext>(params ulong[] roleIds) : PreconditionAttribute<TContext>
    where TContext : IUserContext
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(TContext context, IServiceProvider? services)
    {
        if (context.User is GuildUser gu && gu.RoleIds.Any(roleIds.Contains))
            return new(PreconditionResult.Success);
        return new(PreconditionResult.Fail("You don't have permission to use this command."));
    }
}
```

- The generic context lets one attribute cover commands and components. It can go on a module, command,
  or parameter (parameter preconditions derive from `ParameterPreconditionAttribute<TContext>`).
- The example above only works in guild contexts. DM-invoked member commands need the REST member lookup
  described in §4.12.
- `GuildUser.RoleIds` is inherited from `PartialGuildUser` (verified). In guild interactions `Context.User`
  should be a `GuildInteractionUser`. That type exists and adds a resolved `Permissions` property, but confirm
  it derives from `GuildUser` in the pinned version before relying on the cast.
- Attribute arguments must be compile-time constants. To read role IDs from config, resolve them from
  `serviceProvider` inside the precondition instead of passing them in.
- **Discord-side visibility:** `DefaultGuildPermissions = Permissions.X` on the command hides it from users
  without that permission. Server admins can further restrict commands per role or channel in
  *Server Settings → Integrations*. Treat that as UX only and still enforce in code.

### 4.11 Dependency injection & scopes

- Modules and autocomplete providers use constructor injection and are created per invocation, like transient services.
- With `NetCord.Hosting.Services`, a **DI scope is created per command or interaction** by default (toggle with
  `UseScopes`). Scoped services (e.g. an EF Core `DbContext`) are therefore safe to inject.

### 4.12 Command registration & DM commands

- Hosting auto-registers commands **globally** on startup (`ApplicationCommandServiceOptions.AutoRegisterCommands`, verified).
- **This project uses global registration**, because member commands must work in the bot's DMs, and
  guild-registered commands only appear inside that guild. (Guild-scoped registration exists via
  `ApplicationCommandService<T>.RegisterCommandsAsync(RestClient, ulong applicationId, ulong? guildId, …)`, but don't use it here.)
- Where a command can run is controlled per command with `Contexts` (verified enum `InteractionContextType`:
  `Guild`, `BotDMChannel`, `DMChannel`). Project convention:
  - member commands: `Contexts = [InteractionContextType.BotDMChannel]` (optionally plus `Guild`);
  - staff commands: `Contexts = [InteractionContextType.Guild]`.

  A service-wide default can be set with `ApplicationCommandServiceOptions.DefaultContexts`.
- `IntegrationTypes` (`ApplicationIntegrationType.GuildInstall` / `UserInstall`): this bot is guild-installed only.
- In a DM interaction `Context.Guild` is null and `Context.User` is a plain `User`, not a guild member, so there are
  no `RoleIds`. A role-gating precondition must fetch the member with
  `RestClient.GetGuildUserAsync(configuredGuildId, Context.User.Id)` and treat "not found" (the user left the
  server) as a failure.
- If commands don't appear, refresh the client (Ctrl+R).

### 4.13 REST operations this bot needs (verified signatures, trailing `RestRequestProperties?`/`CancellationToken` omitted)

| Need (old JDA call) | NetCord |
|---|---|
| Send to channel (`sendMessage`) | `RestClient.SendMessageAsync(ulong channelId, MessageProperties)` |
| DM a user (`openPrivateChannel`) | `RestClient.GetDMChannelAsync(ulong userId)` → then send to that channel. Catch `RestException` when DMs are closed (the old code's fallback never fired). |
| Get a member (`getMemberById`) | `RestClient.GetGuildUserAsync(ulong guildId, ulong userId)` |
| List members | `RestClient.GetGuildUsersAsync(ulong guildId, PaginationProperties<ulong>?)` |
| Create request channel (`createCopyOfChannel`) | `RestClient.CreateGuildChannelAsync(ulong guildId, GuildChannelProperties)`. `GuildChannelProperties(string name, ChannelType type)` has `ParentId`, `Topic`, `Position`, `PermissionOverwrites`, … |
| Per-user overwrite (`upsertPermissionOverride`) | `new PermissionOverwriteProperties(ulong id, PermissionOverwriteType.User) { Allowed = Permissions.ViewChannel \| … }`, set at creation or via `RestClient.ModifyGuildChannelPermissionsAsync(ulong channelId, PermissionOverwriteProperties)` |
| Count channels in a category | `RestClient.GetGuildChannelsAsync(ulong guildId)` then filter by `ParentId` (or use the gateway guild cache) |
| Delete channel | `RestClient.DeleteChannelAsync(ulong channelId)` |
| Read history | `RestClient.GetMessagesAsync(ulong channelId, PaginationProperties<ulong>?)`. Content is empty without the intent (see §4.8). |

There is no "copy channel" API. Build the permission overwrites explicitly, or read the template channel's
overwrites and replicate them.

### 4.14 HTTP interactions (serverless option)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDiscordRest().AddHttpApplicationCommands();
var app = builder.Build();
app.AddModules(typeof(Program).Assembly);
app.UseHttpInteractions("/interactions");
await app.RunAsync();
```

- Needs `Discord:PublicKey` in config and the endpoint URL set in the Developer Portal. Discord validates
  the URL immediately, so the app must be running when you save it. Use ngrok for local testing.
- **Requires native libsodium.** Reference the official `libsodium` NuGet package, or install it system-wide.
- Uses the `Http*Context` types (`HttpApplicationCommandContext`, `HttpSlashCommandContext`, …).
- A custom `IHttpInteractionHandler` can be registered with `AddHttpInteractionHandler<T>()`.
- There is no gateway, so no events, no cache, and no online presence. Everything else goes through `RestClient`.

---

## 5. Gotchas checklist

- [ ] Pin exact NetCord beta versions. Re-verify APIs after upgrading.
- [ ] Target `net10.0`.
- [ ] No `MessageContent` intent and no text commands.
- [ ] Respond or defer within 3 s. Defer before any DB + multi-REST workflow.
- [ ] Villager inputs use autocomplete (more than 25 options) and are re-validated server-side.
- [ ] Buttons have no timeout. Enforce expiry and the clicking user in the handler.
- [ ] Set `AllowedMentions` deliberately where users should or shouldn't be pinged.
- [ ] Embed limits: field value ≤ 1024 chars, description ≤ 4096, 25 fields, 6000 total. The old
      `!helpers` output could exceed a field.
- [ ] Discord IDs are `ulong` in NetCord (the old bot used strings).
