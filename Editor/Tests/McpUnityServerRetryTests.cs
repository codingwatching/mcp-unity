using System;
using System.Reflection;
using McpUnity.Unity;
using McpUnity.Utils;
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

        [Test]
        public void FailedStartCleanupStopsTheBackgroundTick()
        {
            MethodInfo cleanup = typeof(McpUnityServer).GetMethod(
                "CleanupFailedStart",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Type tickType = typeof(McpLogger).Assembly.GetType("McpUnity.Utils.McpBackgroundTick");
            MethodInfo stop = tickType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(cleanup);
            Assert.NotNull(stop);
            Assert.IsTrue(Calls(cleanup, stop), "Failed WebSocket starts must stop the background tick before cleaning up server state.");
        }

        private static bool Calls(MethodInfo caller, MethodInfo callee)
        {
            byte[] instructionBytes = caller.GetMethodBody().GetILAsByteArray();
            byte[] methodToken = BitConverter.GetBytes(callee.MetadataToken);

            for (int index = 0; index <= instructionBytes.Length - methodToken.Length - 1; index++)
            {
                if (instructionBytes[index] != 0x28)
                {
                    continue;
                }

                bool tokenMatches = true;
                for (int tokenIndex = 0; tokenIndex < methodToken.Length; tokenIndex++)
                {
                    if (instructionBytes[index + tokenIndex + 1] != methodToken[tokenIndex])
                    {
                        tokenMatches = false;
                        break;
                    }
                }

                if (tokenMatches)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
