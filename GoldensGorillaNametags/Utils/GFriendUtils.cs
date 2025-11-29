using BepInEx.Bootstrap;
using GorillaFriends;

namespace GoldensGorillaNametags.Utils;

internal class GFriendUtils
{
    private static bool Installed(string uuid) => Chainloader.PluginInfos.ContainsKey(uuid);

    public static bool Friend(NetPlayer player) =>
            Installed("net.rusjj.gorillafriends") && Main.IsFriend(player.UserId);

    public static bool RecentlyPlayedWith(NetPlayer player) => Installed("net.rusjj.gorillafriends") &&
                                                               Main.HasPlayedWithUsRecently(
                                                                       player.UserId) == Main.eRecentlyPlayed.Before;

    public static bool Verified(NetPlayer player) =>
            Installed("net.rusjj.gorillafriends") && Main.IsVerified(player.UserId);
}