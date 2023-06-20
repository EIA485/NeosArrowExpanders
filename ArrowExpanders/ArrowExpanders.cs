using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;
using BaseX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArrowExpanders
{
    public class ArrowExpanders : NeosMod
    {
        public override string Name => "ArrowExpanders";
        public override string Author => "eia485";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/EIA485/NeosArrowExpanders/";
        public override void OnEngineInit()
        {
            config = GetConfiguration();
            Harmony harmony = new Harmony("net.eia485.ArrowExpanders");
            harmony.PatchAll();
        }
        static ModConfiguration config;
        [AutoRegisterConfigKey]
        static ModConfigurationKey<bool> KEY_PreferParent = new("PreferParentLeft", "only applies when the current expander is closed, when true the left arrow will prefer the parent of the current expander otherwise it will attempt to find the closest open expander", () => true);
        [AutoRegisterConfigKey]
        static ModConfigurationKey<bool> KEY_InstantAction = new("InstantAction", "when true if the left or right key are pressed and the selected expander is in it's target state then it will jump a new expander and run the keys action on that expander. when false it will only jump to the new expander but not preform an action till the action.", () => true);
        [AutoRegisterConfigKey]
        static ModConfigurationKey<Key?> KEY_EnterBind = new("EnterBind", "what key if any should trigger an attempt to press the closest button?", () => Key.Return);

        static Dictionary<Key, DateTime> pressedAt = new();

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
                    else if (config.GetValue(KEY_EnterBind).HasValue && input.GetKeyDown(config.GetValue(KEY_EnterBind).Value)) key = Key.Return;

                    //the hold function stops randomly, will investigate later. also maybe this should run evey x miliseconds instead of at the current frameate
                    //also maybe should clear all other potental holds when one starts or when a button is pressed
                    if (input.GetKeyDown(Key.RightArrow)) pressedAt[Key.RightArrow] = DateTime.Now;
                    if (input.GetKeyDown(Key.UpArrow)) pressedAt[Key.UpArrow] = DateTime.Now;
                    if (input.GetKeyDown(Key.DownArrow)) pressedAt[Key.DownArrow] = DateTime.Now;

                    if (!key.HasValue)
                    {
                        if(config.GetValue(KEY_EnterBind).HasValue && input.GetKeyDown(config.GetValue(KEY_EnterBind).Value)) key = Key.Return;
                        KeyValuePair<Key, DateTime>? candidate = null;

                        foreach (var pair in pressedAt)
                        {
                            if (input.GetKey(pair.Key) && (DateTime.Now - pair.Value).Milliseconds > 500)
                            {
                                if (candidate.HasValue && candidate.Value.Value.CompareTo(pair.Value) == 1) continue;
                                candidate = pair;
                            }
                        }
                        if (candidate != null) key = candidate.Value.Key;
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
                    if(currentSelection == null) return;
                    currentSelection.World.RunSynchronously(() =>
                    {

                        switch (key)
                        {
                            case Key.LeftArrow:
                                if (currentSelection.SectionRoot.Target != null && currentSelection.IsExpanded) currentSelection.IsExpanded = false;
                                else
                                {
                                    var outerExp = config.GetValue(KEY_PreferParent) ? parentExp(currentSelection.Slot) : null;
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
                                        if (config.GetValue(KEY_InstantAction)) outerExp.IsExpanded = false;
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
                                        if(config.GetValue(KEY_InstantAction)) outerExp.IsExpanded = true;
                                        outerExp.Slot.GetComponent<Button>().IsHovering.Value = true;
                                    }
                                }
                                break;
                            case Key.UpArrow:
                                currentSelection.Slot.GetComponent<Button>().IsHovering.Value = false;
                                var expanders = tool.Laser.CurrentInteractionTarget.Slot.GetComponentsInChildren<Expander>(excludeDisabled: true);
                                int index = expanders.IndexOf(currentSelection) - 1;
                                if (index < 0) index = expanders.Count -1;
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
                catch (Exception e) { Error(e); } // we dont want to disable the userspace component if we throw an exception
            }

        }
        static CommonTool GetCommonTool(UserRoot userRoot, Chirality side) => userRoot.GetRegisteredComponent((CommonTool t) => t.Side.Value == side);
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
        //static Expander Last(Expander cur)
        //{
        //	
        //}
    }
}