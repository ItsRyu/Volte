using System.Threading.Tasks;
using Discord;
using Qmmands;
using Volte.Commands.Preconditions;
using Volte.Data.Models;
using Volte.Data.Models.EventArgs;
using Volte.Data.Models.Results;
using Gommon;

namespace Volte.Commands.Modules
{
    public partial class ModerationModule : VolteModule
    {
        [Command("IdBan")]
        [Description("Bans a user based on their ID.")]
        [Remarks("Usage: |prefix|idban {id} [reason]")]
        [RequireBotGuildPermission(GuildPermission.BanMembers)]
        [RequireGuildModerator]
        public async Task<VolteCommandResult> IdBanAsync(ulong user,
            [Remainder] string reason = "Banned by a Moderator.")
        {
            await Context.Guild.AddBanAsync(user, 0, reason);
            await Context.CreateEmbed("Successfully banned that user from this guild.").SendToAsync(Context.Channel);
            return Ok("Successfully banned that user from this guild.", _ => ModLogService.OnModActionCompleteAsync(
                new ModActionEventArgs(Context, ModActionType.IdBan, user,
                    reason)));
        }
    }
}