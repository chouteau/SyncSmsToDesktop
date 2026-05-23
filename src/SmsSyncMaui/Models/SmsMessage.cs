using System;
using System.ComponentModel.DataAnnotations;

namespace SmsSyncMaui.Models
{
    public class SmsEntry
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        [Required]
        public string Address { get; set; } = string.Empty;

        public string? ContactName { get; set; }

        [Required]
        public string Body { get; set; } = string.Empty;

        public long DateTimestamp { get; set; }

        public int Type { get; set; } // 1 = Reçu (INBOX), 2 = Envoyé (SENT)

        public bool IsSynced { get; set; }

        public bool IsDeleted { get; set; } = false;

        public string? AttachmentBase64 { get; set; }

        public string? AttachmentMimeType { get; set; }

        public DateTime LocalDateTime => DateTimeOffset.FromUnixTimeMilliseconds(DateTimestamp).LocalDateTime.ToLocalTime();

        public string FormattedTime => LocalDateTime.ToString("HH:mm");

        public string FormattedDate => LocalDateTime.ToString("g");
    }
}
