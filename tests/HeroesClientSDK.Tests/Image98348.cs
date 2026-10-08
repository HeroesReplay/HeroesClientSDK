namespace HeroesClientSDK.Tests;

/// <summary>
/// The bytes every reader's discovery needs from 2.57.0.98348 (the current patch), derived on
/// 2026-10-08 from the read-only module image that Save-ModuleImage.ps1 took of a running client
/// (C:\heroesreplay\re\dumps, never committed): the section table; every site of the clock,
/// screen-state, menu-root and game-launch patterns; the screen and game-launch tables with their
/// strings; and the vtable slot, IsA, StaticType and name of each frame class ClientScreen looks
/// for. The rest of the module reads as zero, so it serves discovery as the image does and holds
/// nothing else of the client.
/// </summary>
internal static class Image98348
{
    public const long Base = 0x7FF66CB90000;
    public const long Size = 0x45AE000;
    public const string Version = "2.57.0.98348";

    public static RecordedImage Load() => new(Base, Size, Sections, Bytes, Strings);

    // Name, RVA, virtual size, characteristics.
    private static readonly (string Name, long Rva, int Size, uint Characteristics)[] Sections =
    {
        (".text", 0x1000, 0x262C4BC, 0x60000020),
        (".rdata", 0x262E000, 0x900880, 0x40000040),
        (".data", 0x2F2F000, 0x13499C4, 0xC0000040),
        (".pdata", 0x4279000, 0x2563A4, 0x40000040),
        ("_RDATA", 0x44D0000, 0xB030, 0x40000040),
        (".rsrc", 0x44DC000, 0x58BC8, 0x40000040),
        (".reloc", 0x4535000, 0x78F54, 0x42000040),
    };

    private static readonly (long Rva, string Hex)[] Bytes =
    {
        // screen-state
        (
            0x269544,
            "48 8B 0D E5 72 50 03 48 85 C9 74 25 33 D2 E8 79 DC A5 00 84 C0 74 1A 48 8B 0D CE 72 50 03 E8"
        ),
        // screen-state
        (
            0x26AFA4,
            "48 8B 0D 85 58 50 03 48 85 C9 74 23 33 D2 E8 19 C2 A5 00 84 C0 74 18 48 8B 0D 6E 58 50 03 E8"
        ),
        // screen-state
        (
            0x660AE8,
            "48 8B 0D 41 FD 10 03 48 85 C9 74 1F 33 D2 E8 D5 66 66 00 84 C0 74 14 48 8B 0D 2A FD 10 03 E8"
        ),
        // screen-state
        (
            0x661D5E,
            "48 8B 0D CB EA 10 03 48 85 C9 74 1F 33 D2 E8 5F 54 66 00 84 C0 74 14 48 8B 0D B4 EA 10 03 E8"
        ),
        // screen-state
        (
            0x66614C,
            "48 8B 0D DD A6 10 03 48 85 C9 74 1F 33 D2 E8 71 10 66 00 84 C0 74 14 48 8B 0D C6 A6 10 03 E8"
        ),
        // screen-state
        (
            0x66F591,
            "48 8B 0D 98 12 10 03 48 85 C9 74 24 33 D2 E8 2C 7C 65 00 84 C0 74 19 48 8B 0D 81 12 10 03 E8"
        ),
        // launch-state
        (0x6C41A6, "48 8B 05 FB D9 0A 03 48 8B D9 83 78 20 00 75 12 83 B8 E8 00 01 00 00"),
        // screen-state
        (
            0x6F5A29,
            "48 8B 0D 00 AE 07 03 48 85 C9 74 8E 33 D2 E8 94 17 5D 00 84 C0 74 83 48 8B 0D E9 AD 07 03 E8"
        ),
        // StaticType CStandardDialog
        (
            0x70C210,
            "40 53 48 83 EC 30 8B 05 54 EF C9 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 0F 00 00 00 48 8D 0D 65 03 F5 01"
        ),
        // IsA CStandardDialog
        (0x70C910, "40 53 48 83 EC 20 48 8B DA E8 F2 F8 FF FF"),
        // screen-state
        (
            0x71EBBA,
            "48 8B 0D 6F 1C 05 03 48 85 C9 74 37 33 D2 E8 03 86 5A 00 84 C0 74 2C 48 8B 0D 58 1C 05 03 E8"
        ),
        // screen-state
        (
            0x71EE50,
            "48 8B 0D D9 19 05 03 48 85 C9 74 2D 33 D2 E8 6D 83 5A 00 84 C0 74 22 48 8B 0D C2 19 05 03 E8"
        ),
        // screen-state
        (
            0x71F15E,
            "48 8B 0D CB 16 05 03 48 85 C9 74 2E 33 D2 E8 5F 80 5A 00 84 C0 74 23 48 8B 0D B4 16 05 03 E8"
        ),
        // screen-state
        (
            0x71F248,
            "48 8B 0D E1 15 05 03 48 85 C9 74 58 33 D2 E8 75 7F 5A 00 84 C0 74 4D 48 8B 0D CA 15 05 03 E8"
        ),
        // screen-state
        (
            0x71F78F,
            "48 8B 0D 9A 10 05 03 48 85 C9 74 5C 33 D2 E8 2E 7A 5A 00 84 C0 74 51 48 8B 0D 83 10 05 03 E8"
        ),
        // screen-state
        (
            0x7202FF,
            "48 8B 0D 2A 05 05 03 48 85 C9 74 31 33 D2 E8 BE 6E 5A 00 84 C0 74 26 48 8B 0D 13 05 05 03 E8"
        ),
        // screen-state
        (
            0x7210BE,
            "48 8B 0D 6B F7 04 03 48 85 C9 74 2B 33 D2 E8 FF 60 5A 00 84 C0 74 20 48 8B 0D 54 F7 04 03 E8"
        ),
        // StaticType CEndOfGameAwardsPanel
        (
            0x79A7B0,
            "40 53 48 83 EC 30 8B 05 9C B2 D4 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 15 00 00 00 48 8D 0D ED E5 EC 01"
        ),
        // IsA CEndOfGameAwardsPanel
        (0x8B0720, "40 53 48 83 EC 20 48 8B DA E8 82 A0 EE FF"),
        // launch-state
        (0xCE4EB6, "48 8B 05 EB CC A8 02 48 8B F9 83 78 20 00 75 0D 83 B8 E8 00 01 00 00"),
        // screen-state
        (
            0xCF0C9E,
            "48 8B 0D 8B FB A7 02 48 85 C9 74 55 33 D2 E8 1F 65 FD FF 84 C0 74 4A 48 8B 0D 74 FB A7 02 E8"
        ),
        // screen-state
        (
            0xCF0D5E,
            "48 8B 0D CB FA A7 02 48 85 C9 74 48 33 D2 E8 5F 64 FD FF 84 C0 74 3D 48 8B 0D B4 FA A7 02 E8"
        ),
        // screen-state
        (
            0xCF2F4E,
            "48 8B 0D DB D8 A7 02 48 85 C9 74 27 33 D2 E8 6F 42 FD FF 84 C0 74 1C 48 8B 0D C4 D8 A7 02 E8"
        ),
        // launch-result
        (0xCFB024, "83 F8 02 0F 85 80 00 00 00 48 63 7A 04 8D 47 FF 83 F8 17 77 6D 89 79 08"),
        // screen-state
        (
            0xCFB21A,
            "48 8B 0D 0F 56 A7 02 48 85 C9 74 45 33 D2 E8 A3 BF FC FF 84 C0 74 3A 48 8B 0D F8 55 A7 02 E8"
        ),
        // launch-creator
        (
            0xCFB530,
            "48 83 EC 28 48 83 3D 6C 66 A7 02 00 75 2A B9 58 69 03 00 E8 E8 6F 67 00 48 85 C0 74 14 48 8B C8 E8 0B B0 FF FF 48 89 05 4C 66 A7 02"
        ),
        // screen-state
        (
            0xCFBF75,
            "48 8B 0D B4 48 A7 02 48 85 C9 74 17 33 D2 E8 48 B2 FC FF 84 C0 74 0C 48 8B 0D 9D 48 A7 02 E8"
        ),
        // screen-state
        (
            0xD01B2E,
            "48 8B 0D FB EC A6 02 48 85 C9 74 1F 33 D2 E8 8F 56 FC FF 84 C0 74 14 48 8B 0D E4 EC A6 02 E8"
        ),
        // screen-state
        (
            0xD088A5,
            "48 8B 0D 84 7F A6 02 48 85 C9 74 57 33 D2 E8 18 E9 FB FF 84 C0 74 4C 48 8B 0D 6D 7F A6 02 E8"
        ),
        // screen-state
        (
            0xD08C1B,
            "48 8B 0D 0E 7C A6 02 48 85 C9 74 28 33 D2 E8 A2 E5 FB FF 84 C0 74 1D 48 8B 0D F7 7B A6 02 E8"
        ),
        // StaticType CBattlenetErrorDialog
        (
            0xD136E0,
            "40 53 48 83 EC 30 8B 05 64 B0 A8 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 15 00 00 00 48 8D 0D F5 F1 9E 01"
        ),
        // StaticType CCustomLoadingPanel
        (
            0xD15300,
            "40 53 48 83 EC 30 8B 05 14 8D A8 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 13 00 00 00 48 8D 0D 45 A5 9E 01"
        ),
        // StaticType CDisconnectedDialog
        (
            0xD15540,
            "40 53 48 83 EC 30 8B 05 A4 BB A8 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 13 00 00 00 48 8D 0D A5 F7 9E 01"
        ),
        // StaticType CLoginDialog
        (
            0xD19E60,
            "40 53 48 83 EC 30 8B 05 9C 2E A8 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 0C 00 00 00 48 8D 0D CD 4A 9E 01"
        ),
        // StaticType CProgressBarDialog
        (
            0xD1CB60,
            "40 53 48 83 EC 30 8B 05 BC 16 A8 02 A8 01 75 65 83 C8 01 48 C7 44 24 28 12 00 00 00 48 8D 0D 9D 30 9E 01"
        ),
        // screen-state
        (
            0xD79E96,
            "48 8B 0D 93 69 9F 02 48 85 C9 74 25 33 D2 E8 27 D3 F4 FF 84 C0 74 1A 48 8B 0D 7C 69 9F 02 E8"
        ),
        // glue
        (0xDD6ACA, "49 8B 81 D4 01 00 00 8B 51 08 48 0F A3 D0 73 1D 49 8B 8C D1 F0 01 00 00"),
        // IsA CProgressBarDialog
        (0xDFC990, "40 53 48 83 EC 20 48 8B DA E8 C2 01 F2 FF"),
        // IsA CBattlenetErrorDialog
        (0xE76D00, "40 53 48 83 EC 20 48 8B DA E8 D2 C9 E9 FF"),
        // IsA CCustomLoadingPanel
        (0xED42C0, "40 53 48 83 EC 20 48 8B DA E8 32 10 E4 FF"),
        // IsA CDisconnectedDialog
        (0xED68F0, "40 53 48 83 EC 20 48 8B DA E8 42 EC E3 FF"),
        // screen-state
        (
            0xF078DB,
            "48 8B 0D 4E 8F 86 02 48 85 C9 74 2C 33 D2 E8 E2 F8 DB FF 84 C0 74 21 48 8B 0D 37 8F 86 02 E8"
        ),
        // screen-state
        (
            0xF22D7F,
            "48 8B 0D AA DA 84 02 48 85 C9 74 2B 33 D2 E8 3E 44 DA FF 84 C0 74 20 48 8B 0D 93 DA 84 02 E8"
        ),
        // IsA CLoginDialog
        (0xFAB280, "40 53 48 83 EC 20 48 8B DA E8 D2 EB D6 FF"),
        // screen-state
        (
            0xFDAFDB,
            "48 8B 0D 4E 58 79 02 48 85 C9 74 4B 33 D2 E8 E2 C1 CE FF 84 C0 74 40 48 8B 0D 37 58 79 02 E8"
        ),
        // screen-state
        (
            0xFE2444,
            "48 8B 0D E5 E3 78 02 48 85 C9 74 4B 33 D2 E8 79 4D CE FF 84 C0 74 40 48 8B 0D CE E3 78 02 E8"
        ),
        // screen-state
        (
            0x10C4689,
            "48 8B 0D A0 C1 6A 02 48 85 C9 74 21 33 D2 E8 34 2B C0 FF 84 C0 74 16 48 8B 0D 89 C1 6A 02 E8"
        ),
        // screen-state
        (
            0x10CAAE8,
            "48 8B 0D 41 5D 6A 02 48 85 C9 74 75 33 D2 E8 D5 C6 BF FF 84 C0 74 6A 48 8B 0D 2A 5D 6A 02 E8"
        ),
        // screen-state
        (
            0x10CAFF7,
            "48 8B 0D 32 58 6A 02 48 85 C9 74 31 33 D2 E8 C6 C1 BF FF 84 C0 74 26 48 8B 0D 1B 58 6A 02 E8"
        ),
        // screen-state
        (
            0x10CB7F5,
            "48 8B 0D 34 50 6A 02 48 85 C9 74 6B 33 D2 E8 C8 B9 BF FF 84 C0 74 60 48 8B 0D 1D 50 6A 02 E8"
        ),
        // screen-state
        (
            0x1384147,
            "48 8B 0D E2 C6 3E 02 48 85 C9 74 27 33 D2 E8 76 30 94 FF 84 C0 74 1C 48 8B 0D CB C6 3E 02 E8"
        ),
        // clock
        (0x1AC5E64, "84 C0 74 19 66 0F 6E 05 34 57 8C 01 0F 5B C0 F3 0F 59 05 B1 C7 B7 00"),
        // clock
        (0x1ACBB58, "84 C0 74 15 66 0F 6E 05 40 FA 8B 01 0F 5B C0 F3 0F 59 05 BD 6A B7 00"),
        // screen-state
        (
            0x1EF17C9,
            "48 8B 0D 60 F0 87 01 48 85 C9 74 48 33 D2 E8 F4 59 DD FE 84 C0 74 3D 48 8B 0D 49 F0 87 01 E8"
        ),
        // screen-state
        (
            0x1EF1A2A,
            "48 8B 0D FF ED 87 01 48 85 C9 74 3A 33 D2 E8 93 57 DD FE 84 C0 74 2F 48 8B 0D E8 ED 87 01 E8"
        ),
        // vtable slot CStandardDialog
        (0x265C418, "10 C9 29 6D F6 7F 00 00"),
        // vtable slot CEndOfGameAwardsPanel
        (0x2688868, "20 07 44 6D F6 7F 00 00"),
        // screen-table
        (0x26F6C00, "D8 72 28 6F F6 7F 00 00 29 00 00 00 00 00 00 00"),
        (0x26F6C10, "08 73 28 6F F6 7F 00 00 19 00 00 00 00 00 00 00"),
        (0x26F6C20, "28 73 28 6F F6 7F 00 00 19 00 00 00 00 00 00 00"),
        (0x26F6C30, "48 73 28 6F F6 7F 00 00 23 00 00 00 00 00 00 00"),
        (0x26F6C40, "70 73 28 6F F6 7F 00 00 25 00 00 00 00 00 00 00"),
        (0x26F6C50, "98 73 28 6F F6 7F 00 00 1B 00 00 00 00 00 00 00"),
        (0x26F6C60, "B8 73 28 6F F6 7F 00 00 25 00 00 00 00 00 00 00"),
        (0x26F6C70, "E0 73 28 6F F6 7F 00 00 25 00 00 00 00 00 00 00"),
        (0x26F6C80, "08 74 28 6F F6 7F 00 00 15 00 00 00 00 00 00 00"),
        (0x26F6C90, "20 74 28 6F F6 7F 00 00 15 00 00 00 00 00 00 00"),
        (0x26F6CA0, "38 74 28 6F F6 7F 00 00 21 00 00 00 00 00 00 00"),
        (0x26F6CB0, "60 74 28 6F F6 7F 00 00 15 00 00 00 00 00 00 00"),
        (0x26F6CC0, "78 74 28 6F F6 7F 00 00 13 00 00 00 00 00 00 00"),
        (0x26F6CD0, "90 74 28 6F F6 7F 00 00 29 00 00 00 00 00 00 00"),
        (0x26F6CE0, "C0 74 28 6F F6 7F 00 00 29 00 00 00 00 00 00 00"),
        (0x26F6CF0, "F0 74 28 6F F6 7F 00 00 17 00 00 00 00 00 00 00"),
        (0x26F6D00, "08 75 28 6F F6 7F 00 00 15 00 00 00 00 00 00 00"),
        (0x26F6D10, "20 75 28 6F F6 7F 00 00 15 00 00 00 00 00 00 00"),
        (0x26F6D20, "38 75 28 6F F6 7F 00 00 17 00 00 00 00 00 00 00"),
        (0x26F6D30, "50 75 28 6F F6 7F 00 00 17 00 00 00 00 00 00 00"),
        (0x26F6D40, "68 75 28 6F F6 7F 00 00 19 00 00 00 00 00 00 00"),
        (0x26F6D50, "88 75 28 6F F6 7F 00 00 21 00 00 00 00 00 00 00"),
        (0x26F6D60, "B0 75 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        (0x26F6D70, "D0 75 28 6F F6 7F 00 00 17 00 00 00 00 00 00 00"),
        (0x26F6D80, "E8 75 28 6F F6 7F 00 00 19 00 00 00 00 00 00 00"),
        (0x26F6D90, "08 76 28 6F F6 7F 00 00 25 00 00 00 00 00 00 00"),
        (0x26F6DA0, "30 76 28 6F F6 7F 00 00 17 00 00 00 00 00 00 00"),
        (0x26F6DB0, "48 76 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        (0x26F6DC0, "68 76 28 6F F6 7F 00 00 27 00 00 00 00 00 00 00"),
        (0x26F6DD0, "90 76 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        // launch-table
        (0x26FC600, "08 FF 1C 6F F6 7F 00 00 00 00 00 00 00 00 00 00"),
        (0x26FC610, "F0 CA 28 6F F6 7F 00 00 22 00 00 00 00 00 00 00"),
        (0x26FC620, "18 CB 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        (0x26FC630, "38 CB 28 6F F6 7F 00 00 1D 00 00 00 00 00 00 00"),
        (0x26FC640, "58 CB 28 6F F6 7F 00 00 1C 00 00 00 00 00 00 00"),
        (0x26FC650, "78 CB 28 6F F6 7F 00 00 20 00 00 00 00 00 00 00"),
        (0x26FC660, "A0 CB 28 6F F6 7F 00 00 20 00 00 00 00 00 00 00"),
        (0x26FC670, "C8 CB 28 6F F6 7F 00 00 1A 00 00 00 00 00 00 00"),
        (0x26FC680, "E8 CB 28 6F F6 7F 00 00 21 00 00 00 00 00 00 00"),
        (0x26FC690, "10 CC 28 6F F6 7F 00 00 16 00 00 00 00 00 00 00"),
        (0x26FC6A0, "28 CC 28 6F F6 7F 00 00 1E 00 00 00 00 00 00 00"),
        (0x26FC6B0, "48 CC 28 6F F6 7F 00 00 20 00 00 00 00 00 00 00"),
        (0x26FC6C0, "E0 C7 28 6F F6 7F 00 00 24 00 00 00 00 00 00 00"),
        (0x26FC6D0, "70 CC 28 6F F6 7F 00 00 24 00 00 00 00 00 00 00"),
        (0x26FC6E0, "98 CC 28 6F F6 7F 00 00 20 00 00 00 00 00 00 00"),
        (0x26FC6F0, "C0 CC 28 6F F6 7F 00 00 22 00 00 00 00 00 00 00"),
        (0x26FC700, "E8 CC 28 6F F6 7F 00 00 1D 00 00 00 00 00 00 00"),
        (0x26FC710, "08 CD 28 6F F6 7F 00 00 1D 00 00 00 00 00 00 00"),
        (0x26FC720, "28 CD 28 6F F6 7F 00 00 19 00 00 00 00 00 00 00"),
        (0x26FC730, "48 CD 28 6F F6 7F 00 00 21 00 00 00 00 00 00 00"),
        (0x26FC740, "70 CD 28 6F F6 7F 00 00 1D 00 00 00 00 00 00 00"),
        (0x26FC750, "90 CD 28 6F F6 7F 00 00 1D 00 00 00 00 00 00 00"),
        (0x26FC760, "B0 CD 28 6F F6 7F 00 00 20 00 00 00 00 00 00 00"),
        (0x26FC770, "D8 CD 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        (0x26FC780, "F8 CD 28 6F F6 7F 00 00 1F 00 00 00 00 00 00 00"),
        // vtable slot CProgressBarDialog
        (0x271CDC0, "90 C9 98 6D F6 7F 00 00"),
        // vtable slot CBattlenetErrorDialog
        (0x272D490, "00 6D A0 6D F6 7F 00 00"),
        // vtable slot CCustomLoadingPanel
        (0x273F8B0, "C0 42 A6 6D F6 7F 00 00"),
        // vtable slot CDisconnectedDialog
        (0x2740750, "F0 68 A6 6D F6 7F 00 00"),
        // vtable slot CLoginDialog
        (0x27673A8, "80 B2 B3 6D F6 7F 00 00"),
    };

    private static readonly (long Rva, string Text)[] Strings =
    {
        (0x265C598, "CStandardDialog"),
        (0x2668DC0, "CEndOfGameAwardsPanel"),
        (0x26F72D8, "ScreenBackgroundHero/ScreenBackgroundHero"),
        (0x26F7308, "ScreenSingle/ScreenSingle"),
        (0x26F7328, "ScreenReplay/ScreenReplay"),
        (0x26F7348, "ScreenCreditsHero/ScreenCreditsHero"),
        (0x26F7370, "ScreenCoopCampaign/ScreenCoopCampaign"),
        (0x26F7398, "ScreenLoading/ScreenLoading"),
        (0x26F73B8, "ScreenLoginUnified/ScreenLoginUnified"),
        (0x26F73E0, "ScreenHeroCutscene/ScreenHeroCutscene"),
        (0x26F7408, "ScreenHome/ScreenHome"),
        (0x26F7420, "ScreenHero/ScreenHero"),
        (0x26F7438, "ScreenCollection/ScreenCollection"),
        (0x26F7460, "ScreenLoot/ScreenLoot"),
        (0x26F7478, "ScreenBuy/ScreenBuy"),
        (0x26F7490, "ScreenNavigationHero/ScreenNavigationHero"),
        (0x26F74C0, "ScreenForegroundHero/ScreenForegroundHero"),
        (0x26F74F0, "ScreenScore/ScreenScore"),
        (0x26F7508, "ScreenPlay/ScreenPlay"),
        (0x26F7520, "ScreenSkin/ScreenSkin"),
        (0x26F7538, "ScreenMount/ScreenMount"),
        (0x26F7550, "ScreenBoost/ScreenBoost"),
        (0x26F7568, "ScreenBundle/ScreenBundle"),
        (0x26F7588, "ScreenBundleList/ScreenBundleList"),
        (0x26F75B0, "ScreenCommunity/ScreenCommunity"),
        (0x26F75D0, "ScreenMovie/ScreenMovie"),
        (0x26F75E8, "ScreenBanner/ScreenBanner"),
        (0x26F7608, "ScreenEmoticonPack/ScreenEmoticonPack"),
        (0x26F7630, "ScreenSpray/ScreenSpray"),
        (0x26F7648, "ScreenLootChest/ScreenLootChest"),
        (0x26F7668, "ScreenAnnouncerPack/ScreenAnnouncerPack"),
        (0x26F7690, "ScreenVoiceLine/ScreenVoiceLine"),
        (0x26FC7E0, "@UI/GameLaunchVersionDownloadMessage"),
        (0x26FCAF0, "@UI/GameLaunchGenericLaunchFailure"),
        (0x26FCB18, "@UI/GameLaunchReplayOpenFailure"),
        (0x26FCB38, "@UI/GameLaunchSaveOpenFailure"),
        (0x26FCB58, "@UI/GameLaunchMapOpenFailure"),
        (0x26FCB78, "@UI/GameLaunchTrialDisallowedMap"),
        (0x26FCBA0, "@UI/GameLaunchMapPrefetchFailure"),
        (0x26FCBC8, "@UI/GameLaunchInvalidFiles"),
        (0x26FCBE8, "@UI/GameLaunchTooManyDependencies"),
        (0x26FCC10, "@UI/GameLaunchGameBusy"),
        (0x26FCC28, "@UI/GameLaunchBaseBuildMissing"),
        (0x26FCC48, "@UI/GameLaunchPTRLauncherMissing"),
        (0x26FCC70, "@UI/GameLaunchVersionDownloadFailure"),
        (0x26FCC98, "@UI/GameLaunchVersionLaunchError"),
        (0x26FCCC0, "@UI/GameLaunchDataBuildNumMismatch"),
        (0x26FCCE8, "@UI/GameLaunchModDataMismatch"),
        (0x26FCD08, "@UI/GameLaunchMapDataMismatch"),
        (0x26FCD28, "@UI/GameLaunchNotLicensed"),
        (0x26FCD48, "@UI/GameLaunchLicenseNotValidated"),
        (0x26FCD70, "@UI/GameLaunchUnsupportedInCN"),
        (0x26FCD90, "@UI/GameLaunchUnsupportedInRC"),
        (0x26FCDB0, "@UI/GameLaunchUnsupportedInTrial"),
        (0x26FCDD8, "@UI/GameLaunchUnsupportedNoData"),
        (0x26FCDF8, "@UI/GameLaunchUnsupportedTooOld"),
        (0x26FE950, "CLoginDialog"),
        (0x26FF868, "CCustomLoadingPanel"),
        (0x26FFC20, "CProgressBarDialog"),
        (0x27028F8, "CBattlenetErrorDialog"),
        (0x2704D08, "CDisconnectedDialog"),
    };
}
