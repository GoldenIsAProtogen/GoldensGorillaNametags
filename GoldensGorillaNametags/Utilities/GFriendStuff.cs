//Thank you <3 (https://github.com/sirkingbinx/BingusNametags)
using BepInEx.Bootstrap;

namespace GoldensGorillaNametags.Utilities
{
    internal class GFriendStuff
    {
            private static bool Installed(string uuid) => Chainloader.PluginInfos.ContainsKey(uuid);
            public static bool Friend(NetPlayer player) => Installed("net.rusjj.gorillafriends") && GorillaFriends.Main.IsFriend(player.UserId);
            public static bool RecentlyPlayedWith(NetPlayer player) => Installed("net.rusjj.gorillafriends") && GorillaFriends.Main.HasPlayedWithUsRecently(player.UserId) == GorillaFriends.Main.eRecentlyPlayed.Before;
            public static bool Verified(NetPlayer player) => Installed("net.rusjj.gorillafriends") && GorillaFriends.Main.IsVerified(player.UserId);
    }
}

