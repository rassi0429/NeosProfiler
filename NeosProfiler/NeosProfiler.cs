using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using BaseX;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fleck;

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
    private static FieldInfo? updateIndexField;

    private static WebSocketServer wss;
    private static List<IWebSocketConnection> allSockets;

    public override void OnEngineInit()
    {
      var harmony = new Harmony("com.kka.NeosProfiler");
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

      harmony.PatchAll();

      wss = new WebSocketServer("ws://0.0.0.0:8181");
      allSockets = new List<IWebSocketConnection>();
      wss.Start(socket =>
      {
        socket.OnOpen = () => { allSockets.Add(socket); };
        socket.OnClose = () => { allSockets.Remove(socket); };
      });
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
                    updatable.InternalRunUpdate();
                    updateProperty?.SetValue(__instance, null);
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
              currentlyUpdatingStack.Pop();
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