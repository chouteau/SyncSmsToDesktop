using System.Threading.Tasks;

namespace MobileSmsSync.Services
{
    public interface ISmsSyncPlatformService
    {
        bool IsSmsSupported { get; }
        Task<bool> RequestPermissionsAsync();
        bool ArePermissionsGranted();
        bool IsNotificationAccessGranted();
        void OpenNotificationSettings();
        void StartSyncService(string serverIp);
        void StopSyncService();
    }
}
