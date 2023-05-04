using Newtonsoft.Json;

namespace Elements
{
    public class MetricsSettings : Element
    {
        [JsonProperty("Rentable Area")]
        public double RentableArea { get; set; }

        [JsonProperty("Usable Area")]
        public double UsableArea { get; set; }
    }
}