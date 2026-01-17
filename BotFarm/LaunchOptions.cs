namespace BotFarm
{
    /// <summary>
    /// Command-line launch options for BotFarm
    /// </summary>
    public class LaunchOptions
    {
        /// <summary>
        /// Auto-spawn bots on startup (--auto, -a)
        /// </summary>
        public bool AutoMode { get; set; }

        /// <summary>
        /// Disable UI, run console-only (--no-ui, --console-only)
        /// </summary>
        public bool ConsoleOnly { get; set; }

        /// <summary>
        /// Use Vite dev server instead of production build (--dev-ui)
        /// </summary>
        public bool DevUI { get; set; }

        /// <summary>
        /// Start UI but don't auto-open browser (--no-browser)
        /// </summary>
        public bool NoBrowser { get; set; }

        /// <summary>
        /// Parse launch options from command-line arguments
        /// </summary>
        public static LaunchOptions Parse(string[] args)
        {
            return new LaunchOptions
            {
                AutoMode = Contains(args, "--auto", "-a"),
                ConsoleOnly = Contains(args, "--no-ui", "--console-only"),
                DevUI = Contains(args, "--dev-ui"),
                NoBrowser = Contains(args, "--no-browser")
            };
        }

        private static bool Contains(string[] args, params string[] flags)
        {
            foreach (var arg in args)
            {
                foreach (var flag in flags)
                {
                    if (string.Equals(arg, flag, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
    }
}
