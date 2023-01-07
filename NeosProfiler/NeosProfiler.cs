using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using BaseX;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Timers;
using Fleck;
using System.Threading.Tasks;

namespace NeosProfiler
{
  public class NeosProfiler : NeosMod
  {
    public override string Name => "NeosProfiler";
    public override string Author => "kka429";
    public override string Version => "1.0.0";
    public override string Link => "";

    private static PropertyInfo? worldProperty;
    private static PropertyInfo? updateProperty;
    private static FieldInfo? toUpdateField;
    private static FieldInfo? updateBucketEnumeratorField;
    private static FieldInfo? currentlyUpdatingStackField;
    private static FieldInfo? currentlyUpdating;
    private static FieldInfo? updateIndexField;

    private static WebSocketServer? wss;
    private static List<IWebSocketConnection>? allSockets;

    private static Dictionary<string, long> componentTimeSumDictionary;
    private static Dictionary<string, long> componentTimeMaxDictionary;
    private static Dictionary<string, long> componentTimeMinDictionary;
    private static Dictionary<string, int> componentTimeCountDictionary;

    private static int count = 0; 
    public override void OnEngineInit()
    {
      // When using reflection, you need to use static as much as possible because it damages the FPS!
      worldProperty = typeof(World).GetProperty("LastCommonUpdates")!;
      updateProperty = typeof(UpdateManager).GetProperty("CurrentlyUpdating")!;
      toUpdateField = typeof(UpdateManager).GetField("toUpdate",
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.GetField |
        BindingFlags.FlattenHierarchy |
        BindingFlags.SetField)!;
      updateBucketEnumeratorField = typeof(UpdateManager).GetField("updateBucketEnumerator",
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.GetField |
        BindingFlags.FlattenHierarchy |
        BindingFlags.SetField)!;
      currentlyUpdatingStackField = typeof(UpdateManager).GetField("currentlyUpdatingStack",
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.GetField |
        BindingFlags.FlattenHierarchy |
        BindingFlags.SetField)!;
      updateIndexField = typeof(UpdateManager).GetField("updateIndex",
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.GetField |
        BindingFlags.FlattenHierarchy |
        BindingFlags.SetField)!;
      currentlyUpdating = typeof(UpdateManager).GetField("CurrentlyUpdating",
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.GetField |
        BindingFlags.FlattenHierarchy |
        BindingFlags.SetField)!;
      
      componentTimeSumDictionary = new Dictionary<string, long>();
      componentTimeMaxDictionary = new Dictionary<string, long>();
      componentTimeMinDictionary = new Dictionary<string, long>();
      componentTimeCountDictionary = new Dictionary<string, int>();

      var harmony = new Harmony("com.kka.NeosProfiler");
      harmony.PatchAll();

      wss = new WebSocketServer("ws://0.0.0.0:8181");
      allSockets = new List<IWebSocketConnection>();
      wss.Start(socket =>
      {
        socket.OnOpen = () => { allSockets.Add(socket); };
        socket.OnClose = () => { allSockets.Remove(socket); };
      });
      var timer = new Timer(1000);
      
      timer.Elapsed += (sender, e) =>
      {
          Task.Run(() =>
          {
              StringBuilder str = new StringBuilder();
              foreach (KeyValuePair<string, long> keyValuePair in componentTimeSumDictionary)
              {
                // name sum min max count
                  str.AppendFormat("${0}%{1}%{2}%{3}%{4}", new object[]
                  {
            keyValuePair.Key,
            keyValuePair.Value,
            componentTimeMinDictionary[keyValuePair.Key],
            componentTimeMaxDictionary[keyValuePair.Key],
            componentTimeCountDictionary[keyValuePair.Key]
                  });
              }

              foreach (var webSocketConnection in allSockets)
              {
                  webSocketConnection.Send("#statistics#" + str.ToString());
                  // webSocketConnection.Send(count + "");
              }
              componentTimeSumDictionary.Clear();
              count++;
          });   
      };
      timer.Start();
    }

    [HarmonyPatch(typeof(UpdateManager), "RunUpdates")]
    private class Patch
    {
      // TODO: TL transpiler
      private static bool Prefix(
        UpdateManager __instance,
        ref bool __result
      )
      {
        var toUpdate = (SortedDictionary<int, List<IUpdatable>>)toUpdateField?.GetValue(__instance)!;
        var updateBucketEnumerator =
          (SortedDictionary<int, List<IUpdatable>>.Enumerator)updateBucketEnumeratorField?.GetValue(__instance)!;
        var currentlyUpdatingStack = (Stack<IUpdatable>)currentlyUpdatingStackField?.GetValue(__instance)!;
        var updateIndex = (int)updateIndexField?.GetValue(__instance)!;
        if (toUpdate.Count != 0)
        {
          var keyValuePair = updateBucketEnumerator.Current;
          if (keyValuePair.Value != null)
          {
            try
            {
              for (;;)
              {
                keyValuePair = updateBucketEnumerator.Current;
                if (keyValuePair.Value == null) break;

                keyValuePair = updateBucketEnumerator.Current;
                var value = keyValuePair.Value;
                while (updateIndex < value.Count)
                {
                  var list = value;
                  var lastCommonUpdates = updateIndex;
                  updateIndex = lastCommonUpdates + 1;
                  var updatable = list[lastCommonUpdates];
                  if (!updatable.IsRemoved)
                  {
                    var world = __instance.World;
                    worldProperty?.SetValue(world, world.LastCommonUpdates + 1);
                    updateProperty?.SetValue(__instance, updatable);
                    var timestamp = Stopwatch.GetTimestamp();
                    updatable.InternalRunUpdate();
                    var num = Stopwatch.GetTimestamp() - timestamp;
                    updateProperty?.SetValue(__instance, null);
                    try
                    {
                      long num2;
                      if (componentTimeSumDictionary.TryGetValue(updatable.Name, out num2))
                      {
                        componentTimeSumDictionary[updatable.Name] = num2 + num;
                        componentTimeCountDictionary[updatable.Name] += 1;
                        if (componentTimeMaxDictionary[updatable.Name] < num)
                        {
                          componentTimeMaxDictionary[updatable.Name] = num;
                        }

                        if (componentTimeMinDictionary[updatable.Name] > num)
                        {
                          componentTimeMinDictionary[updatable.Name] = num;
                        }
                      }
                      else
                      {
                        componentTimeSumDictionary.Add(updatable.Name, num);
                        componentTimeMaxDictionary.Add(updatable.Name, num);
                        componentTimeMinDictionary.Add(updatable.Name, num);
                        componentTimeCountDictionary.Add(updatable.Name, 1);
                      }
                    }
                    catch(Exception e)
                    {
                      Error("dictionary write error", e);
                    }
                  }
                }

                if (updateIndex == value.Count)
                {
                  updateIndex = 0;
                  updateBucketEnumerator.MoveNext();
                }
              }
            }
            catch (FatalWorldException)
            {
              throw;
            }
            catch (Exception ex)
            {
              while (currentlyUpdatingStack.Count > 0)
              {
                var current = currentlyUpdatingStack.Pop();
                currentlyUpdating?.SetValue(__instance,current);
              }
              const string text = "Exception when Updating object: ";
              var text2 = __instance.CurrentlyUpdating.ParentHierarchyToString();
              const string text3 = "\n\nException:\n";
              var ex2 = DebugManager.PreprocessException<Exception>(ex);
              Error("Neos RunUpdate Error: " + text + text2 + text3 + ex2?.ToString(), false);
              if (__instance.CurrentlyUpdating is Component component)
              {
                var exceptionAction = ExceptionAction.Disable;
                var customAttribute =
                  component.GetType()
                    .GetCustomAttribute<ExceptionHandlingAttribute>(true, false);
                if (customAttribute != null) exceptionAction = customAttribute.ExceptionAction;

                var flag = component.Slot.IsRootSlot || component.Slot.IsProtected;
                if (exceptionAction == ExceptionAction.Disable)
                {
                  var activeLink = component.EnabledField.ActiveLink;
                  activeLink?.ReleaseLink();

                  component.Enabled = false;
                }
                else if (exceptionAction == ExceptionAction.DeactivateSlot)
                {
                  var activeLink2 = component.EnabledField.ActiveLink;
                  activeLink2?.ReleaseLink();

                  component.Enabled = false;
                  if (!flag)
                  {
                    var activeLink3 = component.Slot.ActiveSelf_Field.ActiveLink;
                    activeLink3?.ReleaseLink();

                    component.Slot.ActiveSelf = false;
                  }
                }
                else if (exceptionAction == ExceptionAction.Destroy)
                {
                  component.Destroy();
                }
                else if (exceptionAction == ExceptionAction.DestroySlot)
                {
                  if (flag)
                    component.Destroy();
                  else
                    component.Slot.Destroy();
                }
                else if (exceptionAction == ExceptionAction.DestroyUserRoot)
                {
                  var activeUserRoot = component.Slot.ActiveUserRoot;
                  (activeUserRoot?.Slot ?? component.Slot.GetObjectRoot())
                    .Destroy();
                }
              }

              updateProperty?.SetValue(__instance, null);
            }

            keyValuePair = updateBucketEnumerator.Current;
            __result = keyValuePair.Value == null;
          }
        }

        __result = true;
        return false;
      }
    }
  }
}