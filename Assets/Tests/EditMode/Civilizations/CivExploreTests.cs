using NUnit.Framework;

namespace WorldBeyond.Tests.Civilizations
{
    public class CivExploreTests
    {
        [Test, Explicit]
        public void Explore([Values(1UL, 42UL, 2026UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            TestContext.WriteLine(r.Describe());
            for (int i = 0; i < 12; i++) { r.Years(5); TestContext.WriteLine(r.Describe()); }
        }
    }
}
