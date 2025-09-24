using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace GoldensGorillaNametags.Utilities
{
    [HarmonyPatch]
    public class VelocityHistoryPatch
    {
        private static MethodInfo targetMethod;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            targetMethod = typeof(VRRig).GetMethod("UpdateVelocityHistory", BindingFlags.NonPublic | BindingFlags.Instance);

            if (targetMethod == null)
            {
                var methods = typeof(VRRig).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (method.Name.Contains("Velocity") || method.Name.Contains("Update") ||
                        (method.ReturnType == typeof(void) && method.GetParameters().Length == 0))
                    {
                        targetMethod = method;
                        break;
                    }
                }
            }

            return targetMethod != null;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() => targetMethod;

        [HarmonyPostfix]
        public static void Postfix(VRRig __instance)
        {
            try
            {
                var velocityHistoryField = typeof(VRRig).GetField("velocityHistoryList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (velocityHistoryField == null) return;

                var velocityHistory = velocityHistoryField.GetValue(__instance);
                if (velocityHistory == null) return;

                Type circularBufferType = velocityHistory.GetType();
                var countProperty = circularBufferType.GetProperty("Count");
                var getItemMethod = circularBufferType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(int) }, null);

                if (countProperty != null && getItemMethod != null)
                {
                    int count = (int)countProperty.GetValue(velocityHistory);
                    if (count > 0)
                    {
                        var firstItem = getItemMethod.Invoke(velocityHistory, new object[] { 0 });
                        if (firstItem != null)
                        {
                            var timeField = firstItem.GetType().GetField("time");
                            if (timeField != null)
                            {
                                float time = (float)timeField.GetValue(firstItem);
                                Main.Instance?.PlrVelUpd(__instance, time);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}