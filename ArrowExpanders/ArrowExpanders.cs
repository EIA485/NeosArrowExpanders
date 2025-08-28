using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Renderite.Shared;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;

namespace ArrowExpanders
{
    [ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
    [BepInDependency(BepInExResoniteShim.PluginMetadata.GUID)]
    public class ArrowExpanders : BasePlugin
    {
        static ConfigEntry<bool> PreferParent, InstantAction;
        static ConfigEntry<int> HoldMilliseconds, HoldRateMinMS;
        static ConfigEntry<Key> EnterBind;
        static ManualLogSource log;
        public override void Load()
        {
            PreferParent = Config.Bind(PluginMetadata.NAME, "PreferParentLeft", true, "only applies when the current expander is closed, when true the left arrow will prefer the parent of the current expander otherwise it will attempt to find the closest open expander");
            InstantAction = Config.Bind(PluginMetadata.NAME, "InstantAction", true, "when true if the left or right key are pressed and the selected expander is in it's target state then it will jump a new expander and run the keys action on that expander. when false it will only jump to the new expander but not preform an action till the action.");
            HoldMilliseconds = Config.Bind(PluginMetadata.NAME, "HoldMilliseconds", 500, "number of milliseconds to wait before starting hold behavior");
            HoldRateMinMS = Config.Bind(PluginMetadata.NAME, "HoldRateMinMS", 0, "minimum number of milliseconds between hold actions");
            EnterBind = Config.Bind(PluginMetadata.NAME, "EnterBind", Key.Return, "what key if any should trigger an attempt to press the closest button?");
            HarmonyInstance.PatchAll();
        }

        static Dictionary<Key, DateTime> pressedAt = new();
        static DateTime? LastHoldTriger;

        [HarmonyPatch(typeof(Userspace), "OnCommonUpdate")]
        class ArrowExpandersPatch
        {
            static void Postfix(Userspace __instance)
            {
                try
                {
                    var input = __instance.InputInterface;
                    Key? key = null;

                    if (input.GetKeyDown(Key.LeftArrow)) { key = Key.LeftArrow; pressedAt[Key.LeftArrow] = DateTime.Now; }
                    else if (input.GetKeyDown(Key.RightArrow)) key = Key.RightArrow;
                    else if (input.GetKeyDown(Key.UpArrow)) key = Key.UpArrow;
                    else if (input.GetKeyDown(Key.DownArrow)) key = Key.DownArrow;
                    else if (input.GetKey(EnterBind.Value)) key = Key.Return;

                    if (input.GetKeyDown(Key.RightArrow)) pressedAt[Key.RightArrow] = DateTime.Now;
                    if (input.GetKeyDown(Key.UpArrow)) pressedAt[Key.UpArrow] = DateTime.Now;
                    if (input.GetKeyDown(Key.DownArrow)) pressedAt[Key.DownArrow] = DateTime.Now;

                    if (!key.HasValue)
                    {
                        if (HoldRateMinMS.Value == 0 || !LastHoldTriger.HasValue || (DateTime.Now - LastHoldTriger.Value).TotalMilliseconds >= HoldRateMinMS.Value) {
                            KeyValuePair<Key, DateTime>? candidate = null;
                            KeyValuePair<Key, DateTime>? latest = null;
                            int delayms = HoldMilliseconds.Value;
                            foreach (var pair in pressedAt)
                            {
                                if (latest == null) latest = pair;
                                else if (pair.Value.CompareTo(latest.Value.Value) > 0) latest = pair;
                                if (input.GetKey(pair.Key) && (DateTime.Now - pair.Value).TotalMilliseconds > delayms)
                                {
                                    if (candidate.HasValue && candidate.Value.Value.CompareTo(pair.Value) == 1) continue;
                                    candidate = pair;
                                }
                            }
                            if (candidate != null && candidate.Value.Key == latest.Value.Key)
                            {
                                key = candidate.Value.Key;
                                LastHoldTriger = DateTime.Now;
                            }
                            else
                            {
                                LastHoldTriger = null;
                            }
                        }
                    }



                    if (!key.HasValue) return;

                    if (Userspace.HasFocus || __instance.Engine.WorldManager.FocusedWorld?.LocalUser.HasActiveFocus() == true) return;

                    var primaryHand = Userspace.GetControllerData(input.PrimaryHand);
                    var tool = primaryHand.userspaceController;
                    bool userSpaceHit = primaryHand.userspaceLaserHitTarget;
                    if (!userSpaceHit && input.VR_Active)
                    {
                        var secondaryHand = Userspace.GetControllerData(GetOther(input.PrimaryHand));
                        if (secondaryHand.userspaceLaserHitTarget)
                        {
                            tool = secondaryHand.userspaceController;
                            userSpaceHit = true;
                        }
                    }
                    if (!userSpaceHit)
                    {
                        bool hit = false;
                        var localUserRoot = __instance.Engine.WorldManager.FocusedWorld?.LocalUser.Root;
                        var primaryTool = GetCommonTool(localUserRoot, input.PrimaryHand);
                        hit = primaryTool.Laser.CurrentInteractionTarget != null && typeof(Canvas).IsAssignableFrom(primaryTool.Laser.CurrentInteractionTarget.GetType());
                        if (hit) tool = primaryTool;
                        else if (input.VR_Active)
                        {
                            var secondaryTool = GetCommonTool(localUserRoot, GetOther(input.PrimaryHand));
                            hit = secondaryTool.Laser.CurrentInteractionTarget != null && typeof(Canvas).IsAssignableFrom(secondaryTool.Laser.CurrentInteractionTarget.GetType());
                            if (hit) tool = secondaryTool;
                            else return;
                        }
                        else return;
                    }
                    var firstRoot = tool.Laser.CurrentInteractionTarget.Slot.GetComponentInChildren<Expander>();

                    var currentSelection = tool.Laser.CurrentInteractionTarget.Slot.GetComponentInChildren<Expander>((e) => e.Slot.GetComponent<Button>()?.IsHovering.Value == true, excludeDisabled: true) ?? firstRoot;
                    if (currentSelection == null) return;
                    currentSelection.World.RunSynchronously(() =>
                    {

                        switch (key)
                        {
                            case Key.LeftArrow:
                                if (currentSelection.SectionRoot.Target != null && currentSelection.IsExpanded) currentSelection.IsExpanded = false;
                                else
                                {
                                    var outerExp = PreferParent.Value ? parentExp(currentSelection.Slot) : null;
                                    if (outerExp == null)
                                    {
                                        var expandas = tool.Laser.CurrentInteractionTarget.Slot.GetComponentsInChildren<Expander>();
                                        int next = -1;
                                        List<Expander> outerlist = new();
                                        foreach (var exp in expandas)
                                        {
                                            if (exp == currentSelection) { next = outerlist.Count - 1; continue; }
                                            if (exp.Slot.IsActive && exp.SectionRoot.Target != null && exp.IsExpanded) outerlist.Add(exp);
                                        }
                                        if (outerlist.Count > 0)
                                        {
                                            if (next < 0 && next + 1 < outerlist.Count) next = next + 1;
                                            outerExp = outerlist[next];
                                        }
                                        else
                                            outerExp = currentSelection;
                                    }
                                    if (outerExp != null)
                                    {
                                        currentSelection.Slot.GetComponent<Button>().IsHovering.Value = false;
                                        if (InstantAction.Value) outerExp.IsExpanded = false;
                                        outerExp.Slot.GetComponent<Button>().IsHovering.Value = true;
                                    }
                                }
                                break;
                            case Key.RightArrow:
                                if (!currentSelection.IsExpanded) currentSelection.IsExpanded = true;
                                else
                                {
                                    var expanderss = tool.Laser.CurrentInteractionTarget.Slot.GetComponentsInChildren<Expander>();
                                    int next = -1;
                                    List<Expander> outerlist = new();
                                    foreach (var exp in expanderss)
                                    {
                                        if (exp == currentSelection) { next = outerlist.Count; continue; }
                                        if (exp.Slot.IsActive && exp.SectionRoot.Target != null && !exp.IsExpanded) outerlist.Add(exp);
                                    }
                                    Expander outerExp = null;
                                    if (outerlist.Count > 0)
                                    {
                                        if (next > outerlist.Count - 1 && next - 1 > -1) next = next - 1;
                                        outerExp = outerlist[next];
                                    }
                                    else
                                        outerExp = currentSelection;


                                    if (outerExp != null)
                                    {
                                        currentSelection.Slot.GetComponent<Button>().IsHovering.Value = false;
                                        if (InstantAction.Value) outerExp.IsExpanded = true;
                                        outerExp.Slot.GetComponent<Button>().IsHovering.Value = true;
                                    }
                                }
                                break;
                            case Key.UpArrow:
                                currentSelection.Slot.GetComponent<Button>().IsHovering.Value = false;
                                var expanders = tool.Laser.CurrentInteractionTarget.Slot.GetComponentsInChildren<Expander>(excludeDisabled: true);
                                int index = expanders.IndexOf(currentSelection) - 1;
                                if (index < 0) index = expanders.Count - 1;
                                var expander = expanders[index];
                                if (expander != null) expander.Slot.GetComponent<Button>().IsHovering.Value = true;
                                break;
                            case Key.DownArrow:
                                currentSelection.Slot.GetComponent<Button>().IsHovering.Value = false;
                                var expanderr = searchAfter<Expander>(currentSelection.Slot) ?? firstRoot;
                                if (expanderr != null) expanderr.Slot.GetComponent<Button>().IsHovering.Value = true;
                                break;
                            case Key.Return://its fairly common for expanders to be next to buttons. may as well add the option to use them aswell.
                                float3 presspointGlobal = currentSelection.Slot.LocalPointToGlobal(float3.Zero);
                                float2 half = new(.5f, .5f);
                                searchInParentsWithSiblings<Button>(currentSelection.Slot).SimulatePress(.1f, new(currentSelection, presspointGlobal, in half, in half));
                                break;
                        }
                    });
                }
                catch (Exception e) { log.LogError(e); } // we dont want to disable the userspace component if we throw an exception
            }

        }
        static InteractionHandler GetCommonTool(UserRoot userRoot, Chirality side) => userRoot.GetRegisteredComponent((InteractionHandler t) => t.Side.Value == side);
        static Chirality GetOther(Chirality cur) => cur == Chirality.Right ? Chirality.Left : Chirality.Right;
        static T searchBefore<T>(Slot cur) where T : Component => searchBefore<T>(cur.Parent, cur.Parent.IndexOfChild(cur) - 1);
        static T searchBefore<T>(Slot root, int index) where T : Component
        {
            for (int i = index; i >= 0; i--)
            {
                var comp = root[i].GetComponentInChildren<T>((f) => f.Slot.IsActive);
                if (comp != null) return comp;
            }
            if (root.Parent == root.World.RootSlot) return null;
            return searchBefore<T>(root);
        }
        static T searchAfter<T>(Slot cur) where T : Component => searchAfter<T>(cur.Parent, cur.Parent.IndexOfChild(cur) + 1);
        static T searchAfter<T>(Slot root, int index) where T : Component
        {
            for (int i = index; i < root.ChildrenCount; i++)
            {
                var comp = root[i].GetComponentInChildren<T>((f) => f.Slot.IsActive);
                if (comp != null) return comp;
            }
            if (root.Parent == root.World.RootSlot) return null;
            return searchAfter<T>(root);
        }
        static T searchInParentsWithSiblings<T>(Slot cur, Predicate<T> filter = null) where T : Component => searchInParentsWithSiblings<T>(cur.Parent, cur, filter);
        static T searchInParentsWithSiblings<T>(Slot cur, Slot last, Predicate<T> filter = null) where T : Component
        {
            foreach (var child in cur.Children)
            {
                if (child == last) continue;
                var comp = child.GetComponentInChildren<T>(filter, excludeDisabled: true);
                if (comp != null) return comp;
            }
            if (last.Parent == last.World.RootSlot) return null;
            return searchInParentsWithSiblings<T>(cur, filter);
        }

        static Expander parentExp(Slot cur) => searchInParentsWithSiblings<Expander>(cur, (e) => cur.IsChildOf(e.SectionRoot.Target ?? cur/* will never be true, just handling nulls*/) && e.Slot.IsActive);

        static string parentString(Slot slot)
        {
            Slot cur = slot;
            string result = "";
            while (cur.Parent != null)
            {
                result = result + '/' + cur.Name + '(' + cur.ReferenceID + ")";
                cur = cur.Parent;
            }
            return result;
        }
    }
}