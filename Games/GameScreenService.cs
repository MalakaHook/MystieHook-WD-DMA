using Raylib_cs;

namespace MystieHook
{
    public static class ScreenService
    {
        private static Misc.ScreenSettings _current;

        public static Misc.ScreenSettings Current => _current;

        public static void UpdateFromMonitor(int mon)
        {
            int w = Raylib.GetMonitorWidth(mon);
            int h = Raylib.GetMonitorHeight(mon);
            _current = new Misc.ScreenSettings(w, h);
        }
    }
}