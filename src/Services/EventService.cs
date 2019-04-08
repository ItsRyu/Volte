using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Humanizer;
using Qmmands;
using Volte.Commands;
using Volte.Data;
using Volte.Data.Objects;
using Volte.Data.Objects.EventArgs;
using Volte.Discord;
using Volte.Extensions;

namespace Volte.Services
{
    [Service("Event", "The main Service for handling some Discord gateway events.")]
    public sealed class EventService
    {
        private readonly LoggingService _logger;

        private readonly AntilinkService _antilink;
        private readonly BlacklistService _blacklist;
        private readonly DatabaseService _db;
        private readonly PingChecksService _pingchecks;
        private readonly CommandService _commandService;

        private readonly bool _shouldStream =
            Config.Streamer.EqualsIgnoreCase("streamer here") || Config.Streamer.IsNullOrWhitespace();

        public EventService(LoggingService loggingService,
            AntilinkService antilinkService,
            BlacklistService blacklistService,
            DatabaseService databaseService,
            PingChecksService pingChecksService,
            CommandService commandService)
        {
            _logger = loggingService;
            _antilink = antilinkService;
            _blacklist = blacklistService;
            _db = databaseService;
            _pingchecks = pingChecksService;
            _commandService = commandService;
        }

        public async Task HandleMessageAsync(MessageReceivedEventArgs args)
        {
            await _blacklist.CheckMessageAsync(args);
            await _antilink.CheckMessageAsync(args);
            await _pingchecks.CheckMessageAsync(args);
            var prefixes = new[] {args.Config.CommandPrefix, $"<@{args.Context.Client.CurrentUser.Id}> "};
            if (CommandUtilities.HasAnyPrefix(args.Message.Content, prefixes, StringComparison.OrdinalIgnoreCase, out _,
                out var cmd))
            {
                var sw = Stopwatch.StartNew();
                var result = await _commandService.ExecuteAsync(cmd, args.Context, VolteBot.ServiceProvider);

                if (result is CommandNotFoundResult) return;
                var targetCommand = _commandService.GetAllCommands()
                                        .FirstOrDefault(x => x.FullAliases.ContainsIgnoreCase(cmd))
                                    ?? _commandService.GetAllCommands()
                                        .FirstOrDefault(x => x.FullAliases.ContainsIgnoreCase(cmd.Split(' ')[0]));
                sw.Stop();
                await OnCommandAsync(targetCommand, result, args.Context, sw);

                if (args.Config.DeleteMessageOnCommand) await args.Context.Message.DeleteAsync();
            }
        }

        public async Task OnReady(ReadyEventArgs args)
        {
            var guilds = args.Client.Guilds.Count;
            var users = args.Client.Guilds.SelectMany(x => x.Users).DistinctBy(x => x.Id).Count();
            var channels = args.Client.Guilds.SelectMany(x => x.Channels).DistinctBy(x => x.Id).Count();

            await _logger.Log(LogSeverity.Info, LogSource.Volte, $"Currently running Volte V{Version.FullVersion}");
            await _logger.Log(LogSeverity.Info, LogSource.Volte, "Use this URL to invite me to your servers:");
            await _logger.Log(LogSeverity.Info, LogSource.Volte, $"{args.Client.GetInviteUrl(true)}");
            await _logger.Log(LogSeverity.Info, LogSource.Volte, $"Logged in as {args.Client.CurrentUser}");
            await _logger.Log(LogSeverity.Info, LogSource.Volte, "Connected to:");
            await _logger.Log(LogSeverity.Info, LogSource.Volte,
                $"    {guilds} server{(guilds.ShouldBePlural() ? "s" : "")}");
            await _logger.Log(LogSeverity.Info, LogSource.Volte,
                $"    {users} user{(users.ShouldBePlural() ? "s" : "")}");

            await _logger.Log(LogSeverity.Info, LogSource.Volte,
                $"    {channels} channel{(channels.ShouldBePlural() ? "s" : "")}");


            if (_shouldStream)
            {
                await args.Client.SetGameAsync(Config.Game);
                await _logger.Log(LogSeverity.Info, LogSource.Volte, $"Set the bot's game to {Config.Game}.");
            }
            else
            {
                var twitchUrl = $"https://twitch.tv/{Config.Streamer}";
                await args.Client.SetGameAsync(Config.Game, twitchUrl, ActivityType.Streaming);
                await _logger.Log(LogSeverity.Info, LogSource.Volte,
                    $"Set the bot's game to \"{ActivityType.Streaming} {Config.Game}, at {twitchUrl}\".");
            }

            foreach (var guild in args.Client.Guilds)
            {
                if (!Config.BlacklistedOwners.Contains(guild.OwnerId)) continue;
                await _logger.Log(LogSeverity.Warning, LogSource.Volte,
                    $"Left guild \"{guild.Name}\" owned by blacklisted owner {guild.Owner}.");
                await guild.LeaveAsync();
            }
        }

        public async Task OnCommandAsync(Command c, IResult res, ICommandContext context, Stopwatch sw)
        {
            var ctx = (VolteContext) context;
            var commandName = ctx.Message.Content.Split(" ")[0];
            var args = ctx.Message.Content.Replace($"{commandName}", "");
            if (string.IsNullOrEmpty(args)) args = "None";
            if (res is FailedResult failedRes)
            {
                await OnCommandFailureAsync(c, failedRes, ctx, args, sw);
                return;
            }

            if (Config.LogAllCommands)
            {
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|  -Command from user: {ctx.User.Username}#{ctx.User.Discriminator} ({ctx.User.Id})");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|     -Command Issued: {c.Name}");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|        -Args Passed: {args.Trim()}");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|           -In Guild: {ctx.Guild.Name} ({ctx.Guild.Id})");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|         -In Channel: #{ctx.Channel.Name} ({ctx.Channel.Id})");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|        -Time Issued: {DateTime.Now}");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|           -Executed: {res.IsSuccessful} ");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    $"|              -After: {sw.Elapsed.Humanize()}");
                await _logger.Log(LogSeverity.Info, LogSource.Module,
                    "-------------------------------------------------");
            }
        }

        public async Task OnModActionCompleteAsync(ModActionEventArgs args)
        {
            var config = _db.GetConfig(args.Guild);
            var c = await args.Guild.GetTextChannelAsync(config.ModerationOptions.ModActionLogChannel);
            if (c is null) return;
            var e = args.Context.CreateEmbedBuilder();
            switch (args.ActionType)
            {
                case ModActionType.Purge:
                {
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator}\n" +
                                            $"**Messages Cleared:** {args.Count}\n" +
                                            $"**Channel:** {args.Context.Channel.Mention}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);
                    return;
                }

                case ModActionType.Delete:
                {
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator}\n" +
                                            $"**Message Deleted:** {args.TargetId}\n" +
                                            $"**Channel:** {args.Context.Channel.Mention}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);
                    return;
                }

                case ModActionType.Kick:
                {
                    config.ModerationOptions.ModActionCaseNumber += 1;
                    _db.UpdateConfig(config);
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator} ({args.Moderator.Id})\n" +
                                            $"**Case:** {config.ModerationOptions.ModActionCaseNumber}\n" +
                                            $"**User:** {args.TargetUser} ({args.TargetId})\n" +
                                            $"**Reason:** {args.Reason}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);
                    return;
                }

                case ModActionType.Softban:
                {
                    config.ModerationOptions.ModActionCaseNumber += 1;
                    _db.UpdateConfig(config);
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator} ({args.Moderator.Id})\n" +
                                            $"**Case:** {config.ModerationOptions.ModActionCaseNumber}\n" +
                                            $"**User:** {args.TargetUser} ({args.TargetId})\n" +
                                            $"**Reason:** {args.Reason}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);

                    return;
                }

                case ModActionType.Ban:
                {
                    config.ModerationOptions.ModActionCaseNumber += 1;
                    _db.UpdateConfig(config);
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator} ({args.Moderator.Id})\n" +
                                            $"**Case:** {config.ModerationOptions.ModActionCaseNumber}\n" +
                                            $"**User:** {args.TargetUser} ({args.TargetId})\n" +
                                            $"**Reason:** {args.Reason}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);

                    return;
                }

                case ModActionType.IdBan:
                {
                    config.ModerationOptions.ModActionCaseNumber += 1;
                    _db.UpdateConfig(config);
                    await e.WithDescription($"**Action:** {args.ActionType}\n" +
                                            $"**Moderator:** {args.Moderator} ({args.Moderator.Id})\n" +
                                            $"**Case:** {config.ModerationOptions.ModActionCaseNumber}\n" +
                                            $"**User:** {args.TargetId}\n" +
                                            $"**Reason:** {args.Reason}\n" +
                                            $"**Time:** {args.Time.FormatFullTime()}, {args.Time.FormatDate()}")
                        .SendToAsync(c);

                    return;
                }
            }
        }

        private async Task OnCommandFailureAsync(Command c, FailedResult res, VolteContext ctx, string args,
            Stopwatch sw)
        {
            var embed = new EmbedBuilder();
            string reason;
            switch (res)
            {
                case CommandNotFoundResult _:
                    reason = "Unknown command.";
                    break;
                case ExecutionFailedResult efr:
                    reason = $"Execution of this command failed.\nFull error message: {efr.Exception.Message}";
                    await _logger.Log(LogSeverity.Error, LogSource.Module, string.Empty, efr.Exception);
                    break;
                case ChecksFailedResult _:
                    reason = "Insufficient permission.";
                    break;
                case ParameterChecksFailedResult pcfr:
                    reason = $"Checks failed on parameter *{pcfr.Parameter.Name}**.";
                    break;
                case ArgumentParseFailedResult apfr:
                    reason = $"Parsing for arguments failed on argument **{apfr.Parameter?.Name}**.";
                    break;
                case TypeParseFailedResult tpfr:
                    reason =
                        $"Failed to parse type **{tpfr.Parameter.Type.FullName}** from parameter **{tpfr.Parameter.Name}**.";
                    break;
                default:
                    reason = "Unknown error.";
                    break;
            }

            if (reason != "Insufficient permission." && reason != "Unknown command.")
            {
                await embed.AddField("Error in Command:", c.Name)
                    .AddField("Error Reason:", reason)
                    .AddField("Correct Usage", c.SanitizeRemarks(ctx))
                    .WithAuthor(ctx.User)
                    .WithErrorColor()
                    .SendToAsync(ctx.Channel);

                if (!Config.LogAllCommands) return;

                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|  -Command from user: {ctx.User.Username}#{ctx.User.Discriminator} ({ctx.User.Id})");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|     -Command Issued: {c.Name}");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|        -Args Passed: {args.Trim()}");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|           -In Guild: {ctx.Guild.Name} ({ctx.Guild.Id})");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|         -In Channel: #{ctx.Channel.Name} ({ctx.Channel.Id})");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|        -Time Issued: {DateTime.Now}");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|           -Executed: {res.IsSuccessful} | Reason: {reason}");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    $"|              -After: {sw.Elapsed.Humanize()}");
                await _logger.Log(LogSeverity.Error, LogSource.Module,
                    "-------------------------------------------------");
            }
        }
    }
}