using System;

namespace libVT100.TestBench.Tests
{
    public class SimpleTestCase : ITestCase
    {
        public string Id { get; }
        public string Category { get; }
        public string Description { get; }
        private readonly Action<TestContext> _action;

        public SimpleTestCase(string id, string category, string description, Action<TestContext> action)
        {
            Id = id;
            Category = category;
            Description = description;
            _action = action;
        }

        public void Run(TestContext context)
        {
            _action(context);
        }
    }
}
