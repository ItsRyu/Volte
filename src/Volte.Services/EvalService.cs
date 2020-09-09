﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Gommon;
using Humanizer;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Qommon.Collections;
using Volte.Commands;
using Volte.Commands.Modules;
using Volte.Core.Helpers;
using Volte.Core.Entities;

namespace Volte.Services
{
    public sealed class EvalService : VolteService
    {
        private readonly Dictionary<ulong, (ulong GuildId, ulong ChannelId, ulong ResultId)> _evals;
        
        private static readonly Regex Pattern = new Regex("[\t\n\r]*`{3}(?:cs)?[\n\r]+((?:.|\n|\t\r)+)`{3}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        
        private readonly LoggingService _logger;

        public EvalService(LoggingService loggingService)
        {
            _evals = new Dictionary<ulong, (ulong GuildId, ulong ChannelId, ulong ResultId)>();
            _logger = loggingService;
        }

        public async Task EvaluateAsync(BotOwnerModule module, string code)
        {
            try
            {
                if (Pattern.IsMatch(code, out var match))
                {
                    code = match.Groups[1].Value;
                }

                await ExecuteScriptAsync(module, code);
            }
            catch (Exception e)
            {
                _logger.Error(LogSource.Module, string.Empty, e);
            }
            finally
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }
        }
        
        private async Task ExecuteScriptAsync(VolteModule module, string code)
        {
            var sopts = ScriptOptions.Default.WithImports(_imports).WithReferences(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(x => !x.IsDynamic && !x.Location.IsNullOrWhitespace()));

            var embed = module.Context.CreateEmbedBuilder();
            DiscordMessage msg;

            if (_evals.Any(x => x.Key == module.Context.Message.Id))
            {
                var (guildId, channelId, resultId) = _evals[module.Context.Message.Id];

                var g = module.Context.Client.GetGuild(guildId);
                var c = g?.GetChannel(channelId);

                if (c is not null)
                {
                    msg = await c.GetMessageAsync(resultId);
                }
                else
                {
                    msg = await embed.WithTitle("Evaluating").WithDescription($"```cs\n{code}```")
                        .SendToAsync(module.Context.Channel);
                }
            }
            else
            {
                msg = await embed.WithTitle("Evaluating").WithDescription($"```cs\n{code}```")
                    .SendToAsync(module.Context.Channel);
            }
            
            try
            {
                var sw = Stopwatch.StartNew();
                var state = await CSharpScript.RunAsync(code, sopts, EvalEnvironment.From(module.Context));
                sw.Stop();
                if (state.ReturnValue is null)
                {
                    await msg.DeleteAsync();
                    await module.Context.Message.CreateReactionAsync(EmojiHelper.BallotBoxWithCheck.ToEmoji());
                }
                else
                {
                    var res = state.ReturnValue switch
                    {
                        string str => str,
                        IEnumerable enumerable => enumerable.Cast<object>().Select(x => $"{x}").Join(", "),
                        DiscordUser user => $"{user} ({user.Id})",
                        DiscordChannel channel => $"#{channel.Name} ({channel.Id})",
                        _ => state.ReturnValue.ToString()
                    };
                    await msg.ModifyAsync(embed: embed.WithTitle("Eval")
                        .AddField("Elapsed Time", $"{sw.Elapsed.Humanize()}", true)
                        .AddField("Return Type", state.ReturnValue.GetType().AsPrettyString(), true)
                        .WithDescription(Formatter.BlockCode(res, "ini")).Build());
                }
            }
            catch (Exception ex)
            {
                await msg.ModifyAsync(embed: embed
                    .AddField("Exception Type", ex.GetType().AsPrettyString(), true)
                    .AddField("Message", ex.Message, true)
                    .WithTitle("Error")
                    .Build());
            }

            if (_evals.All(x => x.Key != module.Context.Message.Id))
            {
                _evals.Add(module.Context.Message.Id, (module.Context.Guild.Id, module.Context.Channel.Id, msg.Id));
            }
            
            
            
        }

        private readonly ReadOnlyList<string> _imports = new ReadOnlyList<string>(new List<string>
            {
                "System", "System.Collections.Generic", "System.Linq", "System.Text", "System.Reflection",
                "System.Diagnostics", "DSharpPlus", "DSharpPlus.Entities", "System.IO",
                "System.Threading", "Gommon", "Volte.Core.Entities", "Humanizer", "System.Globalization",
                "Volte.Core", "Volte.Services", "System.Threading.Tasks", "Qmmands", "Volte.Commands.TypeParsers"
            });
    }
}