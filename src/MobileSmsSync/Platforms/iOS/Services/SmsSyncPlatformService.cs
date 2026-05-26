using System.Threading.Tasks;

namespace MobileSmsSync.Services
{
    public class SmsSyncPlatformService : ISmsSyncPlatformService
    {
        public bool IsSmsSupported => false;

        public Task<bool> RequestPermissionsAsync()
        {
            return Task.FromResult(false);
        }

        public bool ArePermissionsGranted()
        {
            return false;
        }

        public bool IsNotificationAccessGranted()
        {
            return false;
        }

        public void OpenNotificationSettings()
        {
        }

        public void StartSyncService(string serverIp)
        {
        }

        public void StopSyncService()
        {
        }
    }
}
