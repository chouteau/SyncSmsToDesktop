using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Android.Content;

namespace MobileSmsSync.Services
{
    public class SmsSyncPlatformService : ISmsSyncPlatformService
    {
        public bool IsSmsSupported => true;

        public bool ArePermissionsGranted()
        {
            var context = Android.App.Application.Context;
            var smsRead = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.ReadSms) == Android.Content.PM.Permission.Granted;
            var smsReceive = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.ReceiveSms) == Android.Content.PM.Permission.Granted;
            var smsSend = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.SendSms) == Android.Content.PM.Permission.Granted;
            var contactsRead = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.ReadContacts) == Android.Content.PM.Permission.Granted;
            
            bool postNotifications = true;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                postNotifications = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) == Android.Content.PM.Permission.Granted;
            }
            
            return smsRead && smsReceive && smsSend && contactsRead && postNotifications;
        }

        public async Task<bool> RequestPermissionsAsync()
        {
            var statusSms = await Permissions.RequestAsync<Permissions.Sms>();
            var statusContacts = await Permissions.RequestAsync<Permissions.ContactsRead>();
            
            var statusPost = PermissionStatus.Granted;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                statusPost = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            
            return statusSms == PermissionStatus.Granted && 
                   statusContacts == PermissionStatus.Granted && 
                   statusPost == PermissionStatus.Granted;
        }

        public bool IsNotificationAccessGranted()
        {
            var context = Android.App.Application.Context;
            var flat = Android.Provider.Settings.Secure.GetString(context.ContentResolver, "enabled_notification_listeners");
            if (!string.IsNullOrEmpty(flat))
            {
                var names = flat.Split(':');
                foreach (var name in names)
                {
                    var cn = ComponentName.UnflattenFromString(name);
                    if (cn != null && cn.PackageName == context.PackageName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void OpenNotificationSettings()
        {
            var intent = new Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS");
            intent.AddFlags(ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
        }

        public void StartSyncService(string serverIp)
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(SmsSyncService));
            intent.SetAction(SmsSyncService.ActionStart);
            intent.PutExtra(SmsSyncService.ExtraServerIp, serverIp);
            
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }

        public void StopSyncService()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(SmsSyncService));
            intent.SetAction(SmsSyncService.ActionStop);
            context.StartService(intent);
        }
    }
}
