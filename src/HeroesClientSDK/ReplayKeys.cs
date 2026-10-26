using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HeroesClientSDK;

/// <summary>
/// The replay keys the client answers to. Each is a stock Heroes of the Storm replay hotkey,
/// except <see cref="ToggleObserverTools"/>, which is AhliObs' Ctrl+Shift+O.
/// </summary>
public enum ReplayKey
{
    /// <summary>Numpad +: one step up the speed ladder (Normal, Faster, x2, x4, x8).</summary>
    SpeedUp,

    /// <summary>Numpad -: one step down the speed ladder, never below Normal.</summary>
    SpeedDown,

    /// <summary>P: pause or resume.</summary>
    Pause,

    /// <summary>Esc: the game menu.</summary>
    Escape,

    /// <summary>Q: Quit Replay, once the game menu is open.</summary>
    QuitReplay,

    /// <summary>Ctrl+Shift+O: AhliObs' observer tools and replay panel.</summary>
    ToggleObserverTools,

    /// <summary>1: the camera follows player 1.</summary>
    Player1,

    /// <summary>2: the camera follows player 2.</summary>
    Player2,

    /// <summary>3: the camera follows player 3.</summary>
    Player3,

    /// <summary>4: the camera follows player 4.</summary>
    Player4,

    /// <summary>5: the camera follows player 5.</summary>
    Player5,

    /// <summary>6: the camera follows player 6.</summary>
    Player6,

    /// <summary>7: the camera follows player 7.</summary>
    Player7,

    /// <summary>8: the camera follows player 8.</summary>
    Player8,

    /// <summary>9: the camera follows player 9.</summary>
    Player9,

    /// <summary>0: the camera follows player 10.</summary>
    Player10,
}

/// <summary>Sends a replay key to one client.</summary>
public interface IReplayKeys
{
    /// <summary>Sends the key. False when there is no window to send it to.</summary>
    bool Send(ReplayKey key);
}

/// <summary>
/// Posts replay keys (WM_KEYDOWN and WM_KEYUP) to a client's game window and its visible child
/// windows, as HeroesReplay's spectator does. This is the SDK's only input: no mouse, no
/// SendInput, nothing written to the client's memory. Posted keys reach the game with its window
/// in the background or minimised, as over an RDP session without input focus.
/// </summary>
public sealed class WindowReplayKeys : IReplayKeys
{
    private const ushort Control = 0x11;
    private const ushort Shift = 0x10;

    private readonly int processId;
    private IntPtr window;

    /// <summary>Keys for the client with this process id.</summary>
    public WindowReplayKeys(int processId) => this.processId = processId;

    /// <summary>Keys for this client process.</summary>
    public WindowReplayKeys(Process process)
        : this((process ?? throw new ArgumentNullException(nameof(process))).Id) { }

    /// <summary>The virtual-key chord of a replay key, modifiers first.</summary>
    public static IReadOnlyList<ushort> Chord(ReplayKey key) =>
        key switch
        {
            ReplayKey.SpeedUp => [0x6B],
            ReplayKey.SpeedDown => [0x6D],
            ReplayKey.Pause => [0x50],
            ReplayKey.Escape => [0x1B],
            ReplayKey.QuitReplay => [0x51],
            ReplayKey.ToggleObserverTools => [Control, Shift, 0x4F],
            ReplayKey.Player10 => [0x30],
            >= ReplayKey.Player1 and <= ReplayKey.Player9 => [
                (ushort)(0x31 + (key - ReplayKey.Player1)),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    /// <summary>The key that follows player 1 to 10.</summary>
    public static ReplayKey Player(int player) =>
        player is >= 1 and <= 10
            ? ReplayKey.Player1 + (player - 1)
            : throw new ArgumentOutOfRangeException(nameof(player), player, "1 to 10");

    /// <inheritdoc />
    public bool Send(ReplayKey key)
    {
        IReadOnlyList<ushort> chord = Chord(key);
        if (window == IntPtr.Zero || !WindowNativeMethods.IsWindow(window))
        {
            window = GameWindow.Find(processId);
        }

        if (window == IntPtr.Zero)
        {
            return false;
        }

        List<IntPtr> targets = GameWindow.WithVisibleChildren(window);
        foreach (ushort vk in chord)
        {
            Post(targets, vk, down: true);
        }

        for (int i = chord.Count - 1; i >= 0; i--)
        {
            Post(targets, chord[i], down: false);
        }

        return true;
    }

    private static void Post(List<IntPtr> windows, ushort vk, bool down)
    {
        uint scan = WindowNativeMethods.MapVirtualKey(vk, 0);
        uint lParam = 1u | (scan << 16);
        if (!down)
        {
            lParam |= (1u << 30) | (1u << 31);
        }

        int message = down ? WindowNativeMethods.WmKeyDown : WindowNativeMethods.WmKeyUp;
        IntPtr lp = unchecked((IntPtr)(int)lParam);
        foreach (IntPtr hwnd in windows)
        {
            WindowNativeMethods.PostMessage(hwnd, message, vk, lp);
        }
    }
}
