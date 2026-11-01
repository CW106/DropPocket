// 介面語言：繁體中文 / 英文。「自動」依 Windows 的顯示語言決定
using System.Globalization;

namespace DropPocket
{
    static class L
    {
        static bool en;
        static string mode = "auto";

        public static bool En { get { return en; } }

        // auto / zh / en，存在 settings.ini 的 lang=
        public static string Mode { get { return mode; } }

        public static void Set(string value)
        {
            mode = value == "zh" || value == "en" ? value : "auto";
            en = mode == "en" || (mode == "auto" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh");
        }

        public static string T(string zh, string english)
        {
            return en ? english : zh;
        }

        // 英文的單複數：1 item / 2 items
        public static string Count(int n, string zhUnit, string enOne, string enMany)
        {
            return en ? n + " " + (n == 1 ? enOne : enMany) : n + " " + zhUnit;
        }
    }
}
