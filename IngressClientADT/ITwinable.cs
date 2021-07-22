using Azure.DigitalTwins.Core;

namespace IngressClientADT
{
    public interface ITwinable
    {
        public BasicDigitalTwin DigitalTwin { get; set; }

        public string InstanceID { get; }
    }
}
