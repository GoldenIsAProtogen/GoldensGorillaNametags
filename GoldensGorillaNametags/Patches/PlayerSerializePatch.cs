using System;
using HarmonyLib;

namespace GoldensGorillaNametags.Patches;

[HarmonyPatch(typeof(VRRig), nameof(VRRig.SerializeReadShared))]
public static class PlayerSerializePatch
{
    public static bool StopSerialization;

    public static Action<VRRig> OnPlayerSerialize;
    public static bool          Prefix() => !StopSerialization;

    public static void Postfix(VRRig __instance, InputStruct data) =>
            OnPlayerSerialize?.Invoke(__instance);
}