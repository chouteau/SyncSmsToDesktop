using System;
using Android.App;
using Android.Content;
using Android.Provider;
using Android.Util;

namespace MobileSmsSync.Services
{
    [BroadcastReceiver(Name = "com.companyname.mobilesmssync.SmsReceiver", Enabled = true, Exported = true)]
    [IntentFilter(new[] { "android.provider.Telephony.SMS_RECEIVED" }, Priority = 999)]
    public class SmsReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null) return;

            if (intent.Action == "android.provider.Telephony.SMS_RECEIVED")
            {
                try
                {
                    var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
                    if (messages == null) return;

                    foreach (var sms in messages)
                    {
                        if (sms == null) continue;

                        string address = sms.DisplayOriginatingAddress ?? string.Empty;
                        string body = sms.DisplayMessageBody ?? string.Empty;
                        long date = sms.TimestampMillis;

                        Log.Debug("SmsReceiver", $"SMS reçu de {address} : {body}");

                        if (!string.IsNullOrEmpty(address))
                        {
                            var serviceIntent = new Intent(context, typeof(SmsSyncService));
                            serviceIntent.SetAction(SmsSyncService.ActionNewSms);
                            serviceIntent.PutExtra(SmsSyncService.ExtraSmsAddress, address);
                            serviceIntent.PutExtra(SmsSyncService.ExtraSmsBody, body);
                            serviceIntent.PutExtra(SmsSyncService.ExtraSmsDate, date);

                            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                            {
                                context.StartForegroundService(serviceIntent);
                            }
                            else
                            {
                                context.StartService(serviceIntent);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SmsReceiver", $"Erreur réception SMS: {ex.Message}");
                }
            }
        }
    }
}
