using System;
using System.Runtime.InteropServices;

internal static class MenuMask
{
    internal static readonly UIntPtr Marker=new UIntPtr(0x434D574D);
    static Native.Input Key(ushort key,uint flags)
    {
        return new Native.Input {Type=1,Data=new Native.InputUnion {Keyboard=new Native.KeyboardInput {Key=key,Flags=flags,Extra=Marker}}};
    }
    internal static Native.Input[] ReleaseInputs(int key,bool extended)
    {
        // Unassigned VK_E8 cancels the shell's bare Win/Alt menu action without typing text.
        // Replay the original modifier release after the mask so rapid releases retain order.
        return new[]{Key(0xe8,0),Key(0xe8,2),Key((ushort)key,2u|(extended?1u:0u))};
    }
    internal static bool Release(int key,bool extended)
    {
        Native.Input[] inputs=ReleaseInputs(key,extended);
        // On failure, the caller passes through the physical release to avoid a stuck modifier.
        return Native.SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Native.Input)))==inputs.Length;
    }
}
