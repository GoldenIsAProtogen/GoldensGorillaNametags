namespace GoldensGorillaNametags;

internal static class Constants
{
    public const string Guid    = "golden.gorillanametags",
                        Name    = "GoldensGorillaNametags",
                        Version = "4.5.0"; //MAJOR.MINOR.PATCH

    public const string IllegalMod = "#FF0000",
                        LegalMod   = "#00A0FF";

#if ALPHA
    public const string BuildType = "-alpha";
#elif BETA
    public const string BuildType = "-beta";
#elif DEBUG
    public const string BuildType = "-debug";
#elif RELEASE
    public const string BuildType = "";
#endif
}