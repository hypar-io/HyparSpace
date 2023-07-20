using Newtonsoft.Json;

namespace Elements
{
    public class MetricsSettings : Element
    {
        [JsonProperty("Usable Area")]
        public double UsableArea { get; set; }
    }
}