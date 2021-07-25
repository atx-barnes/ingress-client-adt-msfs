using Azure.DigitalTwins.Core;

namespace IngressClientADT
{
    public interface ITwinable
    {
        public BasicDigitalTwin DigitalTwin { get; }

        public string TwinId { get; }
    }
}
