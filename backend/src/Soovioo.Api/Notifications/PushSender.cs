using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace Soovioo.Api.Notifications;

/// <param name="Data">Read by the app when the push is tapped — which page to open.</param>
public record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string> Data);

public enum PushResult
{
    Sent,

    /// <summary>The app was uninstalled or the token expired — forget the device.</summary>
    InvalidToken,
    Failed,
}

public interface IPushSender
{
    Task<PushResult> SendAsync(string token, PushMessage message);
}

public class PushOptions
{
    /// <summary>The Firebase service account key (its JSON). Unset = push
    /// is only written to the log — development, tests.</summary>
    public string? FcmServiceAccountJson { get; set; }
}

/// <summary>FCM HTTP v1 — Android directly, iOS through the APNs key
/// uploaded to the Firebase project.</summary>
public class FcmPushSender(FirebaseMessaging messaging, ILogger<FcmPushSender> logger) : IPushSender
{
    public static FirebaseMessaging Create(string serviceAccountJson) =>
        FirebaseMessaging.GetMessaging(FirebaseApp.Create(
            new AppOptions { Credential = CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson).ToGoogleCredential() },
            "soovioo-push"));

    public async Task<PushResult> SendAsync(string token, PushMessage message)
    {
        try
        {
            await messaging.SendAsync(new Message
            {
                Token = token,
                Notification = new FirebaseAdmin.Messaging.Notification { Title = message.Title, Body = message.Body },
                Data = message.Data,
                Android = new AndroidConfig { Priority = Priority.High },
            });
            return PushResult.Sent;
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch)
        {
            return PushResult.InvalidToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Push wasn't sent.");
            return PushResult.Failed;
        }
    }
}

public class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task<PushResult> SendAsync(string token, PushMessage message)
    {
        logger.LogInformation("Push (not configured) to {Token}: {Title} — {Body}", token[..Math.Min(8, token.Length)], message.Title, message.Body);
        return Task.FromResult(PushResult.Sent);
    }
}

public static class PushServices
{
    public static IServiceCollection AddPush(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Push").Get<PushOptions>() ?? new PushOptions();
        if (string.IsNullOrWhiteSpace(options.FcmServiceAccountJson))
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }
        else
        {
            services.AddSingleton(FcmPushSender.Create(options.FcmServiceAccountJson));
            services.AddSingleton<IPushSender, FcmPushSender>();
        }
        return services;
    }
}
