using System;
using System.Globalization;
using System.Linq;

namespace PowerPal {
    internal static class DisplayScaling {
        // Zero follows the current monitor. Manual values are absolute scales,
        // never a multiplier applied on top of the Windows DPI setting.
        internal static readonly int[] Options={0,100,125,150,175,200,225,250,300};
        public static int Percent { get; private set; }
        public static event Action Changed;
        public static int Normalize(int percent) { return Options.Contains(percent)?percent:0; }
        public static int ResolveDpi(int monitorDpi,int percent) { percent=Normalize(percent); return percent==0?Math.Max(96,monitorDpi):(int)Math.Round(96*percent/100.0); }
        public static string Label(int percent) { return percent==0?"Follow system display scaling settings":(percent/100.0).ToString("0.##",CultureInfo.InvariantCulture)+"x ("+percent+"%)"; }
        public static void Set(int percent) { percent=Normalize(percent); if(Percent==percent) return; Percent=percent; if(Changed!=null) Changed(); }
    }
}
