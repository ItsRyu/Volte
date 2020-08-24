﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gommon;

namespace Volte.Core.Models.Guild
{
    public sealed class GuildData
    {
        public GuildData()
        {
            Configuration = new GuildConfiguration();
            Extras = new GuildExtras();
            UserData = new List<GuildUserData>();
        }

        [JsonPropertyName("id")]
        public ulong Id { get; set; }

        [JsonPropertyName("owner")]
        public ulong OwnerId { get; set; }

        [JsonPropertyName("configuration")]
        public GuildConfiguration Configuration { get; set; }

        [JsonPropertyName("extras")]
        public GuildExtras Extras { get; set; }

        [JsonPropertyName("userdata")]
        public List<GuildUserData> UserData { get; set; }

        public GuildData AddActionForUser(ulong id, ModAction action)
        {
            this.GetUserData(id).Actions.Add(action);
            return this;
        }

        public override string ToString()
            => JsonSerializer.Serialize(this, Config.JsonOptions);
    }
}