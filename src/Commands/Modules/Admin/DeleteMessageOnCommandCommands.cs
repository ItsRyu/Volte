﻿using System.Threading.Tasks;
using Qmmands;
using Volte.Core.Attributes;
using Volte.Commands.Results;

namespace Volte.Commands.Modules
{
    //ignore this class file's godawful name, i need consistency in my life
    public sealed partial class AdminModule
    {
        [Command("DeleteMessageOnCommand", "Dmoc")]
        [Description("Enable/Disable deleting the command message upon execution of a command for this guild.")]
        [Remarks("deletemessageoncommand {Boolean}")]
        [RequireGuildAdmin]
        public Task<ActionResult> DeleteMessageOnCommandAsync(bool enabled)
        {
            Context.GuildData.Configuration.DeleteMessageOnCommand = enabled;
            Db.UpdateData(Context.GuildData);
            return Ok(enabled
                ? "Enabled DeleteMessageOnCommand in this guild."
                : "Disabled DeleteMessageOnCommand in this guild.");
        }

        [Command("DeleteMessageOnTagCommand", "Dmotc")]
        [Description(
            "Enable/Disable deleting the command message upon usage of the tag retrieval command for this guild.")]
        [Remarks("deletemessageontagcommand {Boolean}")]
        public Task<ActionResult> DeleteMessageOnTagCommand(bool enabled)
        {
            Context.GuildData.Configuration.DeleteMessageOnTagCommandInvocation = enabled;
            Db.UpdateData(Context.GuildData);
            return Ok(enabled
                ? "Enabled DeleteMessageOnTagCommand in this guild."
                : "Disabled DeleteMessageOnTagCommand in this guild.");
        }
    }
}