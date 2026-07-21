using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace PartyMoogle.Reply;

/// <summary>
/// Vendored chat-injection primitive. Submits a string to the game's chat box exactly as
/// if the player had typed it and pressed Enter (so a leading '/' is parsed by the game's
/// own command handler). This is the account-threatening, ToS-adversarial core of the reply
/// feature — kept small and in-repo so it stays auditable rather than buried in a dependency.
///
/// MUST be called on the framework thread (see <see cref="ReplySender"/>).
/// </summary>
internal static unsafe class ChatBox
{
    /// <summary>
    /// Validate then inject. Throws <see cref="ArgumentException"/> if the message is empty,
    /// exceeds the game's 500-byte limit, or fails the sanitize invariant — the same guards
    /// XivCommon/ECommons apply. Callers validate the full command beforehand; this is defense
    /// in depth.
    /// </summary>
    public static void SendMessage(string message)
    {
        var bytes = Encoding.UTF8.GetByteCount(message);
        switch (bytes)
        {
            case 0:
                throw new ArgumentException("message is empty", nameof(message));
            case > 500:
                throw new ArgumentException("message is longer than 500 bytes", nameof(message));
        }

        if (message.Length != Sanitize(message).Length)
            throw new ArgumentException("message contained invalid characters", nameof(message));

        var str = Utf8String.FromString(message);
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }

    /// <summary>The single sanitiser used for both cleaning and the length invariant (red-team M4).</summary>
    public static string Sanitize(string message)
        => Service.PluginInterface.Sanitizer.Sanitize(message);
}
