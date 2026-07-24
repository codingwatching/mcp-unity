using System.Reflection;
using McpUnity.Unity;
using NUnit.Framework;

namespace McpUnity.Tests
{
    public class McpUnityServerRetryTests
    {
        [Test]
        public void DelayedStartRetryDelayUsesBoundedBackoff()
        {
            MethodInfo method = typeof(McpUnityServer).GetMethod(
                "GetDelayedStartDelaySeconds",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method, "McpUnityServer should expose a private retry delay helper for bounded restart backoff.");

            double DelayForAttempt(int attempt)
            {
                return (double)method.Invoke(null, new object[] { attempt });
            }

            Assert.AreEqual(0.25d, DelayForAttempt(0), 0.001d);
            Assert.AreEqual(0.25d, DelayForAttempt(1), 0.001d);
            Assert.AreEqual(0.5d, DelayForAttempt(2), 0.001d);
            Assert.AreEqual(1d, DelayForAttempt(3), 0.001d);
            Assert.AreEqual(2d, DelayForAttempt(4), 0.001d);
            Assert.AreEqual(3d, DelayForAttempt(5), 0.001d);
            Assert.AreEqual(5d, DelayForAttempt(6), 0.001d);
            Assert.AreEqual(5d, DelayForAttempt(10), 0.001d);
        }
    }
}
