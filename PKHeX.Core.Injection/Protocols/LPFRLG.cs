using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static PKHeX.Core.Injection.LiveHeXVersion;

namespace PKHeX.Core.Injection;

public sealed class LPFRLG : InjectionBase
{
    public static ReadOnlySpan<LiveHeXVersion> SupportedVersions => [ FRLG_D_v100, FRLG_E_v100, FRLG_F_v100, FRLG_I_v100, FRLG_J_v100, FRLG_S_v100];
    private const uint fakeHeap = 0x2020000;
    private const uint startingOffset = 0x1208000;
    private const int largeBlockSize = 0x3e00;
    private const int offsetPointerSize = 4;
    private const uint slotOffset = 4;
    public uint securitykey = 0;

    public static uint GetB1S1Offset(LiveHeXVersion lv) => lv switch
    {
        LiveHeXVersion.FRLG_E_v100 => 0xBD68D2E0,
        LiveHeXVersion.FRLG_I_v100 => 0xBD68D230,
        LiveHeXVersion.FRLG_D_v100 => 0xBD68D230,
        LiveHeXVersion.FRLG_S_v100 => 0xBD68D230,
        LiveHeXVersion.FRLG_F_v100 => 0xBD68D230,
        LiveHeXVersion.FRLG_J_v100 => 0xBD68D240,
        _ => throw new NotImplementedException(),
    };

    public static uint GetLargeBlockOffset(LiveHeXVersion lv) => lv switch
    {
        LiveHeXVersion.FRLG_E_v100 => 0xBD68D2D8,
        LiveHeXVersion.FRLG_I_v100 => 0xBD68D228,
        LiveHeXVersion.FRLG_D_v100 => 0xBD68D228,
        LiveHeXVersion.FRLG_S_v100 => 0xBD68D228,
        LiveHeXVersion.FRLG_F_v100 => 0xBD68D228,
        LiveHeXVersion.FRLG_J_v100 => 0xBD68D238,
        _ => throw new NotImplementedException(),
    };

    public static uint GetSmallBlockOffset(LiveHeXVersion lv) => lv switch
    {
        LiveHeXVersion.FRLG_E_v100 => 0xBD68D2DC,
        LiveHeXVersion.FRLG_I_v100 => 0xBD68D22C,
        LiveHeXVersion.FRLG_D_v100 => 0xBD68D22C,
        LiveHeXVersion.FRLG_S_v100 => 0xBD68D22C,
        LiveHeXVersion.FRLG_F_v100 => 0xBD68D22C,
        LiveHeXVersion.FRLG_J_v100 => 0xBD68D23C,
        _ => 0,
    };

    private static uint GetBoxOffset(PokeSysBotMini psb)
    {
        var boxoffbytes = psb.com.ReadBytes(GetB1S1Offset(psb.Version), offsetPointerSize);
        var boxoff = BitConverter.ToUInt32(boxoffbytes);
        boxoff -= fakeHeap;
        boxoff += startingOffset;
        return boxoff;
    }

    public override Span<byte> ReadBox(PokeSysBotMini psb, int box, int len, List<byte[]> allpkm)
    {
        var boxoff = GetBoxOffset(psb);
        var boxsize = RamOffsets.GetSlotCount(psb.Version) * RamOffsets.GetSlotSize(psb.Version);
        var boxstart = boxoff + (ulong)(box * boxsize);
        return psb.com.ReadBytes(boxstart + slotOffset, boxsize);
    }

    public override Span<byte> ReadSlot(PokeSysBotMini psb, int box, int slot)
    {
        var boxoff = GetBoxOffset(psb);
        var slotsize = RamOffsets.GetSlotSize(psb.Version);
        var slotstart = boxoff + (ulong)(slot * slotsize);
        return psb.com.ReadBytes(slotstart + slotOffset, slotsize);
    }

    public override void SendSlot(PokeSysBotMini psb, ReadOnlySpan<byte> data, int box, int slot)
    {
        var boxoff = GetBoxOffset(psb);
        var slotsize = RamOffsets.GetSlotSize(psb.Version);
        var slotstart = boxoff + (ulong)(slot * slotsize);
        psb.com.WriteBytes(data, slotstart + slotOffset);
    }

    public override void SendBox(PokeSysBotMini psb, ReadOnlySpan<byte> boxData, int box)
    {
        var boxoff = GetBoxOffset(psb);
        var boxsize = RamOffsets.GetSlotCount(psb.Version) * RamOffsets.GetSlotSize(psb.Version);
        var boxstart = boxoff + (ulong)(box * boxsize);
        psb.com.WriteBytes(boxData, boxstart + slotOffset);
    }
    public override bool ReadBlockFromString(PokeSysBotMini psb, SaveFile sav, string block, out List<byte[]>? read)
    {
        read = null;
        try
        {
            var offsets = SCBlocks[psb.Version].FirstOrDefault(z => z.Display == (block == "Large" ? "Items" : block))
                ?? throw new KeyNotFoundException($"Block '{block}' not found for version {psb.Version}");
            var props = sav.GetType().GetProperty(block) ?? throw new Exception($"{block} not found");
            var blockoff = GetBlockOffset(psb, offsets);
            var size = offsets.Name == "Large" ? largeBlockSize : Marshal.SizeOf(props.PropertyType);
            var ram = psb.com.ReadBytes(blockoff, size);
            var val = ConvertValue(props, ram);
            if (offsets.IsSecured)
                val = (uint)val ^ ((SAV3FRLG)sav).SecurityKey;
            if (offsets.Display == "Large")
                ram.CopyTo(((SAV3)sav).Large);
            else
                props.SetValue(sav, val);
            read = [ram.ToArray()];
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.StackTrace);
            return false;
        }
    }

    public override void WriteBlocksFromSAV(PokeSysBotMini psb, string block, SaveFile sav)
    {
        var props = sav.GetType().GetProperty(block) ?? throw new Exception($"{block} not found");
        var offsets = SCBlocks[psb.Version].FirstOrDefault(z => z.Display == (block == "Large" ? "Items" : block))
            ?? throw new KeyNotFoundException($"Block '{block}' not found for version {psb.Version}");
        var data = block == "Large" ? ((SAV3)sav).Large.ToArray() : props.GetValue(sav) ?? 0;
        if (offsets.IsSecured)
            data = (uint)data ^ ((SAV3FRLG)sav).SecurityKey;
        var blockoff = GetBlockOffset(psb, offsets);
        psb.com.WriteBytes(block == "Large" ? (byte[])data : BitConverter.GetBytes((uint)data), blockoff);
    }

    private static uint GetBlockOffset(PokeSysBotMini psb, BlockData offsets)
    {
        var offsetAccessor = offsets.Name == "Large" ? GetLargeBlockOffset(psb.Version) : GetSmallBlockOffset(psb.Version);
        var blockoffbytes = psb.com.ReadBytes(offsetAccessor, offsetPointerSize);
        var blockoff = BitConverter.ToUInt32(blockoffbytes);
        blockoff -= fakeHeap;
        blockoff += startingOffset;
        blockoff += (uint)offsets.Offset;
        return blockoff;
    }
    public static object ConvertValue(PropertyInfo info, Span<byte> bytes)
    {
        var t = info.PropertyType;
        return t switch
        {
            var _ when t == typeof(UInt16) => BitConverter.ToUInt16(bytes),
            var _ when t == typeof(UInt32) => BitConverter.ToUInt32(bytes),
            var _ when t == typeof(UInt64) => BitConverter.ToUInt64(bytes),
            var _ when t == typeof(UInt128) => BitConverter.ToUInt128(bytes),
            _ => bytes.ToArray(),
        };
    }
    private static BlockData Get(uint offset, string name, string display, SCTypeCode type) => new()
    {
        Name = name,
        Display = display,
        Type = type,
        Offset = offset,
    };
    private static BlockData Get(uint offset, string name, string display, bool secured) => new()
    {
        Name = name,
        Display = display,
        Offset = offset,
        IsSecured = secured,
    };
    private static BlockData Get(uint offset, string name, string display) => new()
    {
        Name = name,
        Display = display,
        Offset = offset,
    };
    public static readonly BlockData[] Blocks_FRLG =
    [
        Get(0x290, "Large", "Money", true),
        Get(0x294, "Large", "Coin", true),
        Get(0, "Large", "Items"),
        Get(0xF20, "Small", "SecurityKey")
    ];
    public static readonly Dictionary<LiveHeXVersion, BlockData[]> SCBlocks = new()
    {
        { FRLG_D_v100, Blocks_FRLG },
        { FRLG_E_v100, Blocks_FRLG },
        { FRLG_F_v100, Blocks_FRLG },
        { FRLG_I_v100, Blocks_FRLG },
        { FRLG_J_v100, Blocks_FRLG },
        { FRLG_S_v100, Blocks_FRLG },
    };
    public override Dictionary<string, string> SpecialBlocks { get; } = new()
    {
        { "Large", "B_OpenItemPouch_Click" },
    };
}

