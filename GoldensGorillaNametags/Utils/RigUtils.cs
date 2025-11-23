using Photon.Pun;

namespace GoldensGorillaNametags.Utils;

public static class RigUtils
{
    // ReSharper disable once InconsistentNaming
    public static int ping(this VRRig rig) =>
            Plugin.Instance.playerPing.TryGetValue(rig, out int ping) ? ping : PhotonNetwork.GetPing();
}