using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Service.Notification;
using Android.Util;

namespace MobileSmsSync.Services
{
    [Service(Name = "com.companyname.mobilesmssync.SmsNotificationListener",
             Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
             Exported = true)]
    [IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
    public class SmsNotificationListener : NotificationListenerService
    {
        public override void OnNotificationPosted(StatusBarNotification? sbn)
        {
            if (sbn == null) return;

            string packageName = sbn.PackageName;
            if (packageName != "com.google.android.apps.messaging") return;

            var notification = sbn.Notification;
            if (notification == null) return;

            var extras = notification.Extras;
            if (extras == null) return;

            string title = extras.GetString(Notification.ExtraTitle) ?? string.Empty;
            string text = extras.GetCharSequence(Notification.ExtraText)?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(text)) return;

            Log.Debug("SmsNotificationListener", $"Notification Google Messages. Expéditeur: {title}, Message: {text}");

            string? address = null;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                var person = (Android.App.Person?)extras.GetParcelable(Notification.ExtraMessagingPerson);
                if (person != null && person.Uri != null)
                {
                    string uriStr = person.Uri;
                    if (uriStr.StartsWith("tel:"))
                    {
                        address = uriStr.Replace("tel:", "");
                    }
                }
            }

            if (string.IsNullOrEmpty(address))
            {
                address = GetPhoneNumberFromName(title);
            }

            if (string.IsNullOrEmpty(address))
            {
                address = title;
            }

            string finalAddress = address.Trim().Replace(" ", "");

            if (!string.IsNullOrEmpty(finalAddress))
            {
                Log.Debug("SmsNotificationListener", $"Redirection RCS de {finalAddress}");
                var intent = new Intent(this, typeof(SmsSyncService));
                intent.SetAction(SmsSyncService.ActionNewSms);
                intent.PutExtra(SmsSyncService.ExtraSmsAddress, finalAddress);
                intent.PutExtra(SmsSyncService.ExtraSmsBody, text);
                intent.PutExtra(SmsSyncService.ExtraSmsDate, sbn.PostTime);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    StartForegroundService(intent);
                }
                else
                {
                    StartService(intent);
                }
            }
        }

        private string? GetPhoneNumberFromName(string contactName)
        {
            string? number = null;
            var uri = ContactsContract.CommonDataKinds.Phone.ContentUri;
            string[] projection = { ContactsContract.CommonDataKinds.Phone.Number };
            string selection = $"{ContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName} = ?";
            string[] selectionArgs = { contactName };

            try
            {
                using var cursor = ContentResolver?.Query(uri, projection, selection, selectionArgs, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    number = cursor.GetString(0);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SmsNotificationListener", $"Erreur recherche contact: {ex.Message}");
            }
            return number;
        }
    }
}
