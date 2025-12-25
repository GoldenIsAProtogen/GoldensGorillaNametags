/* This file is from BingusNametags by SirKingBinx, which is licensed under the MIT License, which you can see below:

MIT License

Copyright (c) 2025 - 2026 SirKingBinx / bingus

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

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
                                                                       player.UserId).recentlyPlayed == Main.eRecentlyPlayed.Before;

    public static bool Verified(NetPlayer player) =>
            Installed("net.rusjj.gorillafriends") && Main.IsVerified(player.UserId);
}