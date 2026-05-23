using System;
using System.ComponentModel.DataAnnotations;

namespace SmsSyncWindows.Models
{
    public class SmsMessage
    {
        [Key]
        public string Id { get; set; } // Identifiant unique (Android ID ou généré)
        
        [Required]
        public string Address { get; set; } // Numéro de téléphone de l'expéditeur/destinataire
        
        public string? ContactName { get; set; } // Nom du contact s'il est connu
        
        [Required]
        public string Body { get; set; } // Contenu du message
        
        public long DateTimestamp { get; set; } // Timestamp Unix en millisecondes
        
        public int Type { get; set; } // 1 = Reçu (INBOX), 2 = Envoyé (SENT)
        
        public bool IsSynced { get; set; } // Statut de synchronisation avec le téléphone

        // Propriété calculée pour un accès pratique
        public DateTime LocalDateTime => DateTimeOffset.FromUnixTimeMilliseconds(DateTimestamp).LocalDateTime.ToLocalTime();

        public string FormattedTime => LocalDateTime.ToString("HH:mm");
        public string FormattedDate => LocalDateTime.ToString("g"); // ex: 22/05/2026 16:30
    }
}
