namespace FluxFlow.Components.Mqtt.Composition;

public static partial class MqttComponentDefinition
{
    public static class Options
    {
        public const string RequestProcessing = "requestProcessing";
        public const string ResultOrder = "resultOrder";
        public const string MaximumConcurrentRequests = "maximumConcurrentRequests";
        public const string MaximumPendingRequests = "maximumPendingRequests";
        public const string Subscription = "subscription";
        public const string WorkflowAcknowledgement = "workflowAcknowledgement";
        public const string BrokerAcknowledgement = "brokerAcknowledgement";
        public const string OutcomeTimeout = "outcomeTimeout";
        public const string MaximumPendingMessages = "maximumPendingMessages";
        public const string MaximumPendingEvents = "maximumPendingEvents";
    }

    public static class Types { public const string Control = "mqtt.command"; public const string Publish = "mqtt.publish"; public const string Trigger = "mqtt.receive"; public const string Events = "mqtt.events"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Ack = "Ack"; public const string Nak = "Nak"; public const string Events = "Events"; }
    public static class Resources { public const string Client = "Client"; public const string Clock = "Clock"; }
    public static class ResourceTypes { public const string Broker = "mqtt.broker"; public const string Client = "mqtt.client"; public const string Subscription = "mqtt.subscription"; public const string Retry = "retry.policy"; }

    public static class ResourceProperties
    {
        public const string Host = "Host";
        public const string Port = "Port";
        public const string UseTls = "UseTls";
        public const string ServerName = "ServerName";
        public const string Strategy = "Strategy";
        public const string InitialDelay = "InitialDelay";
        public const string Increment = "Increment";
        public const string MaximumDelay = "MaximumDelay";
        public const string MaximumAttempts = "MaximumAttempts";
        public const string MaximumDuration = "MaximumDuration";
        public const string ResetAfter = "ResetAfter";
        public const string JitterFactor = "JitterFactor";
        public const string RetryCategories = "RetryCategories";
        public const string TopicFilter = "TopicFilter";
        public const string Qos = "Qos";
        public const string NoLocal = "NoLocal";
        public const string RetainAsPublished = "RetainAsPublished";
        public const string RetainHandling = "RetainHandling";
        public const string ClientId = "ClientId";
        public const string Broker = "Broker";
        public const string Credentials = "Credentials";
        public const string Username = "Username";
        public const string Password = "Password";
        public const string Certificates = "Certificates";
        public const string CleanStart = "CleanStart";
        public const string KeepAlive = "KeepAlive";
        public const string LastWill = "LastWill";
        public const string AutoConnect = "AutoConnect";
        public const string Reconnect = "Reconnect";
        public const string Subscriptions = "Subscriptions";
    }
}
