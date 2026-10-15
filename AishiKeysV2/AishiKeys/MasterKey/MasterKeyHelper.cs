using System.Collections.Generic;
using AishiKeys.Config;

namespace AishiKeys.MasterKey
{
    public static class MasterKeyHelper
    {
        public static string GetMasterKey(UltraKeys masterKey)
        {
            switch (masterKey)
            {
                case UltraKeys.AishiMK:
                    return "694479f61287f9a2b1060a7f";
                case UltraKeys.UltraKeyOne:
                    return "69439100a320344f805ba61f";
                case UltraKeys.UltraKeyTwo:
                    return "6944b4b15a0413f1b7f50724";
                case UltraKeys.UltraKeyThree:
                    return "69860f957139b00c69e69cb7";
                case UltraKeys.UltraKeyFour:
                    return "6a0a5091eb733b78d06f82b2";
                case UltraKeys.UltraKeyFive:
                    return "999a5a06e1a05ca89b786f34";
                default:
                    return "694479f61287f9a2b1060a7f";
            }
        }

        public static readonly HashSet<string> AllMasterKeyIds = new HashSet<string>
        {
            "694479f61287f9a2b1060a7f",
            "69439100a320344f805ba61f",
            "6944b4b15a0413f1b7f50724",
            "69860f957139b00c69e69cb7",
            "6a0a5091eb733b78d06f82b2",
            "999a5a06e1a05ca89b786f34"
        };
    }
}
