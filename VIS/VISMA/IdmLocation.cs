using System.Text.Json.Serialization;

namespace FimSync_Ezma
{
    public class IdmLocation
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
