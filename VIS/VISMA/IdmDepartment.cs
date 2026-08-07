using System.Text.Json.Serialization;

namespace FimSync_Ezma
{
    public class IdmDepartment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("number")]
        public string Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("validFrom")]
        public string ValidFrom { get; set; }

        [JsonPropertyName("validTo")]
        public string ValidTo { get; set; }
    }
}
