using System;
using System.Linq;
using System.Text;

namespace HeroesClientSDK;

/// <summary>
/// One message dialog shown at the top of the client's UI, with the text its labels hold in
/// memory (UTF-8, read with <c>ReadProcessMemory</c>; nothing reads the screen). The text is the
/// client's own string with its markup, such as <c>&lt;n/&gt;</c> for a line break.
/// </summary>
/// <param name="Dialog">The dialog's class, such as <c>CBattlenetErrorDialog</c>.</param>
/// <param name="Title">The title label's text, or null when it does not read.</param>
/// <param name="Message">The message label's text, or null when it does not read.</param>
public readonly record struct DialogMessage(string Dialog, string Title, string Message)
{
    /// <summary>True when the title or the message read and is not empty.</summary>
    public bool HasText => !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Message);

    /// <summary>The title and the message that read, on one line, for a text rule or a log.</summary>
    public string Text =>
        string.Join(" ", new[] { Title, Message }.Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>
/// Reads a standard dialog's title and message from its labels (<see cref="DialogTextLayout"/>).
/// A label counts only when its class (named by its <c>IsA</c>, like every frame) ends with
/// "Label", so a dialog that is not a standard dialog reads as no text, never as a wrong one.
/// Measured live on 2.57.0.98348 (2026-10-08): the shown <c>CStandardDialog</c> for a
/// 2.57.0.98297 replay read "The version of Heroes of the Storm required to play this game is not
/// available.", and the hidden <c>CLoginDialog</c> read "Authentication" / "Connecting...".
/// </summary>
internal static class DialogText
{
    /// <summary>The longest text read, in bytes. Dialog messages are a few hundred.</summary>
    public const int MaxBytes = 4096;

    private static readonly UTF8Encoding Strict = new(false, true);

    public static DialogMessage Read(
        IProcessMemory memory,
        DialogTextLayout layout,
        long dialog,
        string className,
        Func<long, string> classOf
    )
    {
        string title = Label(memory, layout, dialog + layout.TitleLabelOffset, classOf);
        string message = Label(memory, layout, dialog + layout.MessageLabelOffset, classOf);
        return new DialogMessage(className, title, message);
    }

    /// <summary>
    /// The text of the label whose pointer is at <paramref name="slot"/>: empty when the label
    /// has no string, null when the slot is not a label or its text does not read.
    /// </summary>
    internal static string Label(
        IProcessMemory memory,
        DialogTextLayout layout,
        long slot,
        Func<long, string> classOf
    )
    {
        if (
            !TryPointer(memory, slot, out long label)
            || label == 0
            || !TryPointer(memory, label, out long vtable)
            || vtable == 0
        )
        {
            return null;
        }

        string name = classOf(vtable);
        if (name == null || !name.EndsWith("Label", StringComparison.Ordinal))
        {
            return null;
        }

        if (
            !TryPointer(memory, label + layout.LabelTextOffset, out long text)
            || text == 0
            || !TryPointer(memory, text + layout.TextStringOffset, out long block)
        )
        {
            return null;
        }

        // No string block is the client's shared empty string.
        return block == 0 ? string.Empty : String(memory, block + layout.StringOffset);
    }

    /// <summary>
    /// The client's string at <paramref name="at"/>: a 32-bit length times 4, 32-bit flags, then
    /// the UTF-8 bytes, or a pointer to them when flags bit 1 is set. Null when it does not read,
    /// is longer than <see cref="MaxBytes"/>, or is not UTF-8.
    /// </summary>
    internal static string String(IProcessMemory memory, long at)
    {
        byte[] header = new byte[8];
        if (at <= 0 || !memory.TryRead(at, header))
        {
            return null;
        }

        long length = BitConverter.ToUInt32(header, 0) >> 2;
        uint flags = BitConverter.ToUInt32(header, 4);
        if (length == 0)
        {
            return string.Empty;
        }

        if (length > MaxBytes)
        {
            return null;
        }

        long data = at + 8;
        if ((flags & 2) != 0 && (!TryPointer(memory, at + 8, out data) || data == 0))
        {
            return null;
        }

        byte[] bytes = new byte[length];
        if (!memory.TryRead(data, bytes))
        {
            return null;
        }

        try
        {
            string text = Strict.GetString(bytes);
            return text.IndexOf('\0') >= 0 ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool TryPointer(IProcessMemory memory, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (address <= 0 || !memory.TryRead(address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }
}
