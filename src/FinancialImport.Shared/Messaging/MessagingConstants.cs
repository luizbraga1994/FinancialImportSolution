namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Logical names only. Actual queue/exchange/topic names come from
/// configuration (<see cref="RabbitMqOptions"/> and <see cref="KafkaOptions"/>),
/// so this file intentionally contains no runtime-sensitive strings.
/// </summary>
public static class MessagingChannels
{
    public static class RabbitMq
    {
        public const string ImportProcessCommand   = "import.process.command";
        public const string ImportReprocessCommand = "import.reprocess.command";
        public const string SapDispatchCommand     = "sap.dispatch.command";
        public const string AuditWriteCommand      = "audit.write.command";
    }

    public static class Kafka
    {
        public const string ImportEvents    = "import.events";
        public const string SapEvents       = "sap.events";
        public const string SecurityEvents  = "security.events";
        public const string AuditEvents     = "audit.events";
    }
}
