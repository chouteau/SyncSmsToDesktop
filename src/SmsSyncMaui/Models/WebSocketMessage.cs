namespace SmsSyncMaui.Models
{
    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
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

    public class SyncStartPayload
    {
        public int Total { get; set; }
    }

    public class SyncProgressPayload
    {
        public int Current { get; set; }
        public int Total { get; set; }
    }
}
