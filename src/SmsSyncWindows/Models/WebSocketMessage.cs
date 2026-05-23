namespace SmsSyncWindows.Models
{
    public class WebSocketMessage
    {
        public string Type { get; set; } // ex: "sms_history", "new_sms", "send_sms", "send_sms_status", "connect"
        public string Payload { get; set; } // Contenu JSON sérialisé spécifique au type
    }

    public class SendSmsPayload
    {
        public string Address { get; set; } // Numéro destinataire
        public string Body { get; set; }    // Texte du message
        public string RequestId { get; set; } // Identifiant pour corréler la réponse de statut
    }

    public class SendSmsStatusPayload
    {
        public string RequestId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
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
