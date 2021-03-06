﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Gommon;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Qmmands;
using Volte.Commands;
using Volte.Commands.Modules;
using Volte.Core.Models;
using Volte.Core.Models.EventArgs;
using Volte.Core.Models.Guild;

namespace Volte.Services
{
    public sealed class BlacklistService : VolteEventService
    {
        private readonly LoggingService _logger;
        private readonly DatabaseService _db;
        private readonly IServiceProvider _provider;

        public BlacklistService(LoggingService loggingService, DatabaseService databaseService,
            IServiceProvider provider)
        {
            _logger = loggingService;
            _db = databaseService;
            _provider = provider;
        }

        public override Task DoAsync(EventArgs args)
            => CheckMessageAsync(args.Cast<MessageReceivedEventArgs>() ?? throw new InvalidOperationException($"AutoRole was triggered with a null event. Expected: {nameof(MessageReceivedEventArgs)}, Received: {args.GetType().Name}"));

        private async Task CheckMessageAsync(MessageReceivedEventArgs args)
        {
            if (!args.Data.Configuration.Moderation.Blacklist.Any()) return;
            _logger.Debug(LogSource.Volte, "Checking a message for blacklisted words.");
            if (args.Context.User.IsAdmin(args.Context))
            {
                _logger.Debug(LogSource.Volte, "Aborting check because the user is a guild admin.");
                return;
            }

            foreach (var word in args.Data.Configuration.Moderation.Blacklist.Where(word => args.Message.Content.ContainsIgnoreCase(word)))
            {
                await args.Message.TryDeleteAsync();
                _logger.Debug(LogSource.Volte, $"Deleted a message for containing {word}.");
                var action = args.Data.Configuration.Moderation.BlacklistAction;
                if (action is BlacklistAction.Nothing) return;
                await action.PerformAsync(args.Context, args.Message.Author.Cast<SocketGuildUser>(), word);
            }
        }
    }
}