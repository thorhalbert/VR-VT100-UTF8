using System;
using System.Text;
using libVT100.TestBench.Harness;
using libVT100.TestBench.Tests;
using libVT100.TestBench.Tests.Suites;

namespace libVT100.TestBench
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length == 0 || args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                string? filter = args.Length > 1 ? args[1] : null;
                var runner = new TestRunner();
                runner.RegisterAll(HelloWorldSuite.GetTests());
                runner.RegisterAll(VT220Suite.GetTests());
                runner.RegisterAll(VT220SuitePart2.GetTests());
                runner.RegisterAll(VT220SuitePart3.GetTests());
                runner.RegisterAll(XTermSuite.GetTests());
                runner.RegisterAll(XTermSuitePart2.GetTests());
                runner.RegisterAll(KittySuite.GetTests());
                runner.RegisterAll(KittySuitePart2.GetTests());
                runner.RegisterAll(OobSuite.GetTests());
                runner.RegisterAll(OobSuitePart2.GetTests());
                runner.RegisterAll(KittyGraphicsSuite.GetTests());
                runner.RegisterAll(KittyGraphicsSuitePart2.GetTests());
                runner.RegisterAll(KittyGraphicsSuitePart3.GetTests());
                runner.RegisterAll(RobustnessSuite.GetTests());
                return runner.Run(filter);
            }

            if (args[0].Equals("eval", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: dotnet run --project libvt100.TestBench -- eval \"<sequence>\" [-w width] [-h height]");
                    return 1;
                }

                string sequence = args[1];
                int width = 80;
                int height = 24;

                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "-w" && i + 1 < args.Length && int.TryParse(args[i + 1], out int w))
                        width = w;
                    if (args[i] == "-h" && i + 1 < args.Length && int.TryParse(args[i + 1], out int h))
                        height = h;
                }

                using var session = new HeadlessTerminalSession(width, height);
                session.Feed(sequence);

                Console.WriteLine();
                Console.WriteLine(FrameBufferVisualizer.RenderToString(session.Buffer));

                string emitted = session.GetEmittedOutputString();
                if (!string.IsNullOrEmpty(emitted))
                {
                    Console.WriteLine($"Emitted Output (Terminal -> Host): {emitted}");
                }
                return 0;
            }

            if (args[0].Equals("demo", StringComparison.OrdinalIgnoreCase))
            {
                RunDemo();
                return 0;
            }

            Console.WriteLine("Commands:");
            Console.WriteLine("  test [filter]                  - Run test suite (default)");
            Console.WriteLine("  eval \"<sequence>\" [-w W] [-h H] - Evaluate escape sequence and display visual framebuffer");
            Console.WriteLine("  demo                           - Run a visual demo of VT100 capabilities");
            return 0;
        }

        private static void RunDemo()
        {
            Console.WriteLine("=== Demo: VT100 Hello World & Positioning ===");
            using var session = new HeadlessTerminalSession(40, 8);
            session.Feed("\e[H\e[2J");
            session.Feed("\e[2;5H\e[1m*** HELLO VT100 WORLD ***\e[0m");
            session.Feed("\e[4;3H\e[31mRed Text\e[0m \e[32mGreen Text\e[0m \e[34mBlue Text\e[0m");
            session.Feed("\e[6;10H\e[4mUnderlined Message\e[0m");
            Console.WriteLine(FrameBufferVisualizer.RenderToString(session.Buffer));
        }
    }
}
