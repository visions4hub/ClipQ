using System.Windows.Forms;

namespace ClipQ
{
    // The interface language is chosen at build time: build.bat compiles an Arabic build (default)
    // and an English one (/define:EN). The Arabic build lays out right-to-left, the English one left-to-right.
    static class L
    {
#if EN
        public static readonly bool Rtl = false;
#else
        public static readonly bool Rtl = true;
#endif

        public static string T(string arabic, string english) { return Rtl ? arabic : english; }

        public static RightToLeft Dir { get { return Rtl ? RightToLeft.Yes : RightToLeft.No; } }

        public static MessageBoxOptions MsgOptions
        {
            get { return Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0; }
        }

        // Flags for single-line text that starts at the line's start (right in Arabic, left in English).
        public static TextFormatFlags StartAligned
        {
            get { return Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left; }
        }
    }
}
