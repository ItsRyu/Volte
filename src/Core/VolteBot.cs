using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Gommon;
using Microsoft.Extensions.DependencyInjection;
using Qmmands;
using Volte.Core.Models;
using Volte.Services;
using Console = Colorful.Console;

namespace Volte.Core
{
    public class VolteBot
    {
        public static Task StartAsync()
        {
            Console.Title = "Volte";
            Console.CursorVisible = false;
            return new VolteBot().LoginAsync();
        }

        private IServiceProvider _provider;
        private DiscordShardedClient _client;
        private CancellationTokenSource _cts;

        private static IServiceProvider BuildServiceProvider()
            => new ServiceCollection() 
                .AddAllServices()
                .BuildServiceProvider();

        private VolteBot() 
            => Console.CancelKeyPress += (s, _) => _cts.Cancel();

        private async Task LoginAsync()
        {
            if (!Config.StartupChecks()) return; 

            Config.Load();

            if (!Config.IsValidDiscordToken()) return;

            _provider = BuildServiceProvider();
            
            _client = _provider.Get<DiscordShardedClient>();
            _cts = _provider.Get<CancellationTokenSource>();
            var logger = _provider.Get<LoggingService>();

            await _client.StartAsync();
            await _client.StartAsync().ContinueWith(_ => _client.UpdateStatusAsync(userStatus: UserStatus.Online));

            Initialize(_provider);

            try
            {
                await Task.Delay(-1, _cts.Token);
            }
            catch (TaskCanceledException) //this exception always occurs when CancellationTokenSource#Cancel() is called; so we put the shutdown logic inside the catch block
            {
                logger.Critical(LogSource.Volte,
                    "Bot shutdown requested; shutting down and cleaning up.");
                await ShutdownAsync(_client, _provider);
            }
        }

        // ReSharper disable SuggestBaseTypeForParameter
        public static async Task ShutdownAsync(DiscordShardedClient client, IServiceProvider provider)
        {
            if (Config.GuildLogging.EnsureValidConfiguration(client, out var channel))
            {
                await new DiscordEmbedBuilder()
                    .WithErrorColor()
                    .WithAuthor(client.CurrentApplication.Owners.FirstOrDefault()?.Username ?? "<N/A>")
                    .WithDescription(
                        $"Volte {Version.FullVersion} is shutting down at **{DateTimeOffset.UtcNow.FormatFullTime()}, on {DateTimeOffset.UtcNow.FormatDate()}**. I was online for **{Process.GetCurrentProcess().CalculateUptime()}**!")
                    .SendToAsync(channel);
            }
            
            foreach (var disposable in provider.GetServices<IDisposable>())
            {
                disposable?.Dispose();
            }
            
            await client.UpdateStatusAsync(userStatus: UserStatus.Invisible);
            foreach (var (_, aclient) in client.ShardClients)
            {
                await aclient.DisconnectAsync();
                aclient.Dispose();
            }
            Environment.Exit(0);
        }
        
        public void Initialize(IServiceProvider provider)
        {
            var commandService = provider.Get<CommandService>();
            var logger = provider.Get<LoggingService>();
            
            var sw = Stopwatch.StartNew();
            var l = commandService.AddTypeParsersAsync();
            sw.Stop();
            logger.Info(LogSource.Volte, $"Loaded TypeParsers: [{l.Select(x => x.SanitizeParserName()).Join(", ")}] in {sw.ElapsedMilliseconds}ms.");
            sw = Stopwatch.StartNew();
            var loaded = commandService.AddModules(GetType().Assembly, null, module =>
                {
                    module.WithRunMode(RunMode.Sequential);
                });
            sw.Stop();
            logger.Info(LogSource.Volte,
                $"Loaded {loaded.Count} modules and {loaded.Sum(m => m.Commands.Count)} commands in {sw.ElapsedMilliseconds}ms.");
            _client.RegisterVolteEventHandlers(provider);
        }
    }
}