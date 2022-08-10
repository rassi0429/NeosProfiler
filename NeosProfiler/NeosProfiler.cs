using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using BaseX;
using System.Collections.Generic;

namespace NeosProfiler
{
  public class NeosProfiler : NeosMod
  {
    public override string Name => "NeosProfiler";
    public override string Author => "kka429";
    public override string Version => "1.0.0";
    public override string Link => "";


    public override void OnEngineInit()
    {
      var harmony = new Harmony("com.kka.NeosProfiler");
      harmony.PatchAll();
    }

    [HarmonyPatch(typeof(UpdateManager), "RunUpdates")]
    private class Patch
    {
      private static bool Prefix(UpdateManager __instance,
        SortedDictionary<int, List<IUpdatable>> ___toUpdate,
        SortedDictionary<int, List<IUpdatable>>.Enumerator ___updateBucketEnumerator,
        int ___updateIndex,
        Stack<IUpdatable> ___currentlyUpdatingStack,
        ref bool __result
      )
      {
        if (___toUpdate.Count != 0)
        {
          var keyValuePair = ___updateBucketEnumerator.Current;
          if (keyValuePair.Value != null)
          {
            try
            {
              for (;;)
              {
                keyValuePair = ___updateBucketEnumerator.Current;
                if (keyValuePair.Value == null) break;

                keyValuePair = ___updateBucketEnumerator.Current;
                var value = keyValuePair.Value;
                while (___updateIndex < value.Count)
                {
                  var list = value;
                  var lastCommonUpdates = ___updateIndex;
                  ___updateIndex = lastCommonUpdates + 1;
                  var updatable = list[lastCommonUpdates];
                  if (!updatable.IsRemoved)
                  {
                    var world = __instance.World;
                    lastCommonUpdates = world.LastCommonUpdates;
                    // AccessTools.PropertyGetter(typeof(World), "LastCommonUpdates");
                    var worldProperty = world.GetType().GetProperty("LastCommonUpdates");
                    worldProperty.SetValue(world, world.LastCommonUpdates + 1, null);
                    // world.LastCommonUpdates = lastCommonUpdates + 1;
                    var currentlyUpdating =
                      __instance.GetType().GetProperty("CurrentlyUpdating");
                    currentlyUpdating.SetValue(__instance, (IUpdatable)updatable, null);
                    // ___CurrentlyUpdating = updatable;
                    updatable.InternalRunUpdate();
                    currentlyUpdating.SetValue(__instance, (IUpdatable)null, null);
                    // ___CurrentlyUpdating = null;
                  }
                }

                if (___updateIndex == value.Count)
                {
                  ___updateIndex = 0;
                  ___updateBucketEnumerator.MoveNext();
                }
              }
            }
            catch (FatalWorldException)
            {
              throw;
            }
            catch (Exception ex)
            {
              //this.RestoreRootCurrentlyUpdating();
              ___currentlyUpdatingStack.Pop();
              var text = "Exception when Updating object: ";
              var text2 = __instance.CurrentlyUpdating.ParentHierarchyToString();
              var text3 = "\n\nException:\n";
              var ex2 = DebugManager.PreprocessException<Exception>(ex);
              Msg(text + text2 + text3 + (ex2 != null ? ex2.ToString() : null), false);
              var component = __instance.CurrentlyUpdating as Component;
              if (component != null)
              {
                var exceptionAction = ExceptionAction.Disable;
                var customAttribute =
                  component.GetType()
                    .GetCustomAttribute<ExceptionHandlingAttribute>(true, false); // GetCustomAttribute(true, false);
                if (customAttribute != null) exceptionAction = customAttribute.ExceptionAction;

                var flag = component.Slot.IsRootSlot || component.Slot.IsProtected;
                switch (exceptionAction)
                {
                  case ExceptionAction.Disable:
                  {
                    var activeLink = component.EnabledField.ActiveLink;
                    if (activeLink != null) activeLink.ReleaseLink();

                    component.Enabled = false;
                    break;
                  }
                  case ExceptionAction.DeactivateSlot:
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

                    break;
                  }
                  case ExceptionAction.Destroy:
                    component.Destroy();
                    break;
                  case ExceptionAction.DestroySlot:
                    if (flag)
                      component.Destroy();
                    else
                      component.Slot.Destroy();

                    break;
                  case ExceptionAction.DestroyUserRoot:
                  {
                    var activeUserRoot = component.Slot.ActiveUserRoot;
                    ((activeUserRoot != null ? activeUserRoot.Slot : null) ?? component.Slot.GetObjectRoot())
                      .Destroy();
                    break;
                  }
                }
              }

              var currentlyUpdating = __instance.GetType().GetProperty("CurrentlyUpdating");
              currentlyUpdating.SetValue(__instance, (IUpdatable)null, null);
            }

            keyValuePair = ___updateBucketEnumerator.Current;
            __result = keyValuePair.Value == null;
          }
        }

        __result = true;
        return false;
      }
    }
  }
}