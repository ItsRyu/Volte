﻿using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Volte.Core.Models.Guild
{
    public sealed class GuildExtras
    {
        internal GuildExtras()
        {
            SelfRoles = new List<string>();
            Tags = new List<Tag>();
            Warns = new List<Warn>();
        }

        [JsonPropertyName("self_roles")]
        public List<string> SelfRoles { get; set; }

        [JsonPropertyName("tags")]
        public List<Tag> Tags { get; set; }
        
        [JsonPropertyName("starboard_entries")]
        public List<StarboardEntry> StarboardEntries { get; set; }

        [JsonPropertyName("warns")]
        public List<Warn> Warns { get; set; }

        [JsonPropertyName("mod_log_case_number")]
        public ulong ModActionCaseNumber { get; set; }
        
        [JsonPropertyName("auto_parse_quote_urls")]
        public bool AutoParseQuoteUrls { get; set; }
        
        public override string ToString()
            => JsonSerializer.Serialize(this, Config.JsonOptions);
    }
}