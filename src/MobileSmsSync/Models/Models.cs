using System;

namespace MobileSmsSync.Models
{
    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    public class SmsMessage
    {
        public string Id { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? ContactName { get; set; }
        public string Body { get; set; } = string.Empty;
        public long DateTimestamp { get; set; }
        public int Type { get; set; } // 1 = INBOX, 2 = SENT
        public bool IsSynced { get; set; }
        public string? AttachmentBase64 { get; set; }
        public string? AttachmentMimeType { get; set; }
    }

    public class SendSmsPayload
    {
        public string Address { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
    }

    public class SendSmsStatusPayload
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class FavoriteContact
    {
        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
    }
}
