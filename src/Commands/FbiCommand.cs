namespace UB3RB0T.Commands
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Discord;
    using Newtonsoft.Json;
    using Serilog;

    public class FbiCommand : IDiscordCommand
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiEndpoint = "https://api.fbi.gov/@wanted?page=1&sort_order=desc&sort_on=modified&pageSize=5";

        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            try
            {
                var response = await httpClient.GetAsync(apiEndpoint);
                
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Log.Warning("FBI API returned status code: {StatusCode}", response.StatusCode);
                    return new CommandResponse { Text = "Sorry, the FBI database is currently unavailable. Try again later." };
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                var wantedResult = JsonConvert.DeserializeObject<WantedResultSet>(jsonContent);

                if (wantedResult?.Items == null || !wantedResult.Items.Any())
                {
                    return new CommandResponse { Text = "No wanted persons found. Either the FBI caught everyone or their API is having issues." };
                }

                var settings = SettingsConfig.GetSettings(context.GuildChannel?.Guild.Id ?? 0);

                if (context.Channel is Discord.ITextChannel textChannel && 
                    textChannel.GetCurrentUserPermissions().EmbedLinks && 
                    settings.PreferEmbeds)
                {
                    var embedBuilder = new EmbedBuilder
                    {
                        Title = "FBI Most Wanted - Top 5",
                        Description = "Here are the top 5 most recently updated FBI Most Wanted persons:",
                        Color = new Color(0x1f4e79), // rough estimate of fbi blue idfk im too lazy to google a press kit
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "Data from FBI.gov",
                            IconUrl = "https://www.fbi.gov/image-repository/fbi-seal.jpg"
                        }
                    };

                    foreach (var person in wantedResult.Items.Take(5))
                    {
                        var rewardText = person.RewardText ?? "Reward information not specified";
                        var fieldValue = $"**Classification:** {person.PersonClassification ?? "Unknown"}\n" +
                                       $"**Reward:** {rewardText}\n" +
                                       $"**Status:** {person.Status ?? "Unknown"}";

                        if (!string.IsNullOrEmpty(person.Caution))
                        {
                            fieldValue += $"\n**Caution:** {person.Caution.Substring(0, Math.Min(person.Caution.Length, 100))}...";
                        }

                        embedBuilder.AddField(
                            name: person.Title ?? "Unknown Subject",
                            value: fieldValue,
                            inline: false
                        );
                    }

                    return new CommandResponse { Embed = embedBuilder.Build() };
                }
                else
                {
                    var textResponse = "**FBI Most Wanted - Top 5**\n\n";
                    
                    int counter = 1;
                    foreach (var person in wantedResult.Items.Take(5))
                    {
                        textResponse += $"**{counter}.** {person.Title ?? "Unknown Subject"}\n";
                        textResponse += $"   Classification: {person.PersonClassification ?? "Unknown"}\n";
                        textResponse += $"   Reward: {person.RewardText ?? "Not specified"}\n";
                        textResponse += $"   Status: {person.Status ?? "Unknown"}\n\n";
                        counter++;
                    }

                    textResponse += "Report tips to the FBI | Data from FBI.gov";
                    return new CommandResponse { Text = textResponse };
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing FBI most wanted request");
                return new CommandResponse { Text = "Something went wrong while fetching the FBI most wanted list. The bad guys are still out there though." };
            }
        }
    }
    public class WantedResultSet
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("items")]
        public WantedPerson[] Items { get; set; }
    }

    public class WantedPerson
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("reward_text")]
        public string RewardText { get; set; }

        [JsonProperty("reward_min")]
        public int? RewardMin { get; set; }

        [JsonProperty("reward_max")]
        public int? RewardMax { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("person_classification")]
        public string PersonClassification { get; set; }

        [JsonProperty("poster_classification")]
        public string PosterClassification { get; set; }

        [JsonProperty("caution")]
        public string Caution { get; set; }

        [JsonProperty("warning_message")]
        public string WarningMessage { get; set; }

        [JsonProperty("details")]
        public string Details { get; set; }

        [JsonProperty("additional_information")]
        public string AdditionalInformation { get; set; }

        [JsonProperty("sex")]
        public string Sex { get; set; }

        [JsonProperty("race")]
        public string Race { get; set; }

        [JsonProperty("hair")]
        public string Hair { get; set; }

        [JsonProperty("eyes")]
        public string Eyes { get; set; }

        [JsonProperty("height_min")]
        public int? HeightMin { get; set; }

        [JsonProperty("height_max")]
        public int? HeightMax { get; set; }

        [JsonProperty("weight_min")]
        public int? WeightMin { get; set; }

        [JsonProperty("weight_max")]
        public int? WeightMax { get; set; }

        [JsonProperty("age_min")]
        public int? AgeMin { get; set; }

        [JsonProperty("age_max")]
        public int? AgeMax { get; set; }

        [JsonProperty("dates_of_birth_used")]
        public string[] DatesOfBirthUsed { get; set; }

        [JsonProperty("place_of_birth")]
        public string PlaceOfBirth { get; set; }

        [JsonProperty("locations")]
        public string[] Locations { get; set; }

        [JsonProperty("field_offices")]
        public string[] FieldOffices { get; set; }

        [JsonProperty("possible_countries")]
        public string[] PossibleCountries { get; set; }

        [JsonProperty("possible_states")]
        public string[] PossibleStates { get; set; }

        [JsonProperty("occupations")]
        public string[] Occupations { get; set; }

        [JsonProperty("scars_and_marks")]
        public string ScarsAndMarks { get; set; }

        [JsonProperty("modified")]
        public DateTime? Modified { get; set; }

        [JsonProperty("publication")]
        public DateTime? Publication { get; set; }
    }
}
