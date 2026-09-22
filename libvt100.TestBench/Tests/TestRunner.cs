using System;
using System.Collections.Generic;
using System.Linq;

namespace libVT100.TestBench.Tests
{
    public class TestRunner
    {
        private readonly List<ITestCase> _tests = new List<ITestCase>();

        public void Register(ITestCase test)
        {
            _tests.Add(test);
        }

        public void RegisterAll(IEnumerable<ITestCase> tests)
        {
            _tests.AddRange(tests);
        }

        public int Run(string? filter = null, bool verbose = false)
        {
            var selected = string.IsNullOrWhiteSpace(filter)
                ? _tests
                : _tests.Where(t => t.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                    t.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            Console.WriteLine($"================================================================================");
            Console.WriteLine($" Running libvt100 Test Suite ({selected.Count} tests selected)");
            Console.WriteLine($"================================================================================");

            int passed = 0;
            int failed = 0;

            foreach (var test in selected)
            {
                var ctx = new TestContext();
                Console.Write($"[{test.Category}] {test.Id,-40} ... ");

                try
                {
                    test.Run(ctx);
                    if (ctx.Passed)
                    {
                        passed++;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("PASS");
                        Console.ResetColor();
                    }
                    else
                    {
                        failed++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("FAIL");
                        Console.ResetColor();
                        Console.WriteLine($"  Error: {ctx.FailureMessage}");
                        if (!string.IsNullOrEmpty(ctx.FailureDiff))
                        {
                            Console.WriteLine(ctx.FailureDiff);
                        }
                    }
                }
                catch (TestAssertionException)
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAIL");
                    Console.ResetColor();
                    Console.WriteLine($"  Error: {ctx.FailureMessage}");
                    if (!string.IsNullOrEmpty(ctx.FailureDiff))
                    {
                        Console.WriteLine(ctx.FailureDiff);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR");
                    Console.ResetColor();
                    Console.WriteLine($"  Unhandled Exception: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }

            Console.WriteLine($"================================================================================");
            Console.WriteLine($" Results: {passed} passed, {failed} failed (Total: {selected.Count})");
            Console.WriteLine($"================================================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}
