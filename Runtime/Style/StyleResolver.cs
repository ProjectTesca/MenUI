using System.Collections.Generic;

namespace MenUI
{
    /// Resolves the computed style of every element whose style changed (and
    /// of its descendants when an inherited value changed). A change to a
    /// layout property invalidates layout; anything else only repaints.
    internal sealed class StyleResolver
    {
        private MenUIContext currentContext;
        private readonly float[] previousNumbers = new float[StyleProperty.Count];
        private readonly int[] previousUnits = new int[StyleProperty.Count];
        private readonly UnityEngine.Color[] previousColors = new UnityEngine.Color[StyleProperty.Count];
        // The initial values every resolve starts from (copied in bulk).
        private readonly ComputedStyle defaults = NewDefaults();

        private static ComputedStyle NewDefaults()
        {
            ComputedStyle template = new ComputedStyle();
            template.FillDefaults();
            return template;
        }

        /// Resolves the queued elements, shallowest first. Descendants are
        /// only visited when they must be: an inherited value changed, or the
        /// subtree was just attached. Other dirty descendants are queued
        /// themselves.
        internal void ResolveQueue(MenUIContext context)
        {
            currentContext = context;
            List<Element> queue = context.styleQueue;
            SortByDepth(queue);
            for (int i = 0; i < queue.Count; i++)
            {
                Element e = queue[i];
                e.queuedForStyle = false;
                if (!e.styleDirty && !e.subtreeStyleDirty)
                {
                    continue;   // done as part of an ancestor
                }
                Element top = e;
                while (top.parent != null)
                {
                    top = top.parent;
                }
                if (top != context.root && top != context.overlayRoot)
                {
                    continue;   // detached: resolved when attached again
                }
                ResolveTree(e, e.parent != null ? e.parent.computed : null, false, false, context);
            }
            queue.Clear();
        }

        /// `rematch`: the subtree was (re)attached or an ancestor's classes
        /// changed, so style sheet rules must be matched again below.
        internal void ResolveTree(Element element, ComputedStyle parent, bool force, bool rematch, MenUIContext context)
        {
            bool subtree = element.subtreeStyleDirty;
            bool forceChildren = subtree;
            bool rematchChildren = rematch || subtree;
            element.subtreeStyleDirty = false;
            if (rematch || subtree || element.matchDirty)
            {
                Match(element, context);
            }
            if (force || forceChildren || element.styleDirty)
            {
                if (Resolve(element, parent, context.scale))
                {
                    forceChildren = true;
                }
            }
            if (!forceChildren && !force)
            {
                return;
            }
            for (int i = 0; i < element.children.Count; i++)
            {
                ResolveTree(element.children[i], element.computed, true, rematchChildren, context);
            }
        }

        /// Collects the sheet rules `element` matches (candidates by its tag
        /// and classes), sorted by specificity, then sheet, then rule order.
        private void Match(Element element, MenUIContext context)
        {
            element.matchDirty = false;
            List<StyleSheet> sheets = context.styleSheets;
            if (sheets.Count == 0 && element.matchedRules == null)
            {
                return;
            }
            List<StyleRule> matched = element.matchedRules;
            if (matched == null)
            {
                matched = new List<StyleRule>();
            }
            matched.Clear();
            int mask = 0;
            for (int s = 0; s < sheets.Count; s++)
            {
                StyleSheet sheet = sheets[s];
                mask |= Collect(sheet.universal, element, matched);
                List<StyleRule> bucket;
                if (element.tag != null && sheet.byTag.TryGetValue(element.tag, out bucket))
                {
                    mask |= Collect(bucket, element, matched);
                }
                if (element.classes != null)
                {
                    for (int c = 0; c < element.classes.Count; c++)
                    {
                        if (sheet.byClass.TryGetValue(element.classes[c], out bucket))
                        {
                            mask |= Collect(bucket, element, matched);
                        }
                    }
                }
            }
            SortRules(matched);
            element.matchedRules = matched.Count > 0 ? matched : null;
            if ((mask & PseudoState.ExitTo) != 0)
            {
                context.RegisterExit(element);
            }
            if (mask != element.stateRuleMask)
            {
                element.stateRuleMask = mask;
                context.hitVersion++;   // :hover/:active rules make an element hittable
            }
        }

        private static int Collect(List<StyleRule> candidates, Element element, List<StyleRule> into)
        {
            int mask = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                StyleRule rule = candidates[i];
                if (!into.Contains(rule) && rule.Matches(element))
                {
                    into.Add(rule);
                    mask |= rule.States;
                }
            }
            return mask;
        }

        private static bool Before(StyleRule a, StyleRule b)
        {
            if (a.specificity != b.specificity) return a.specificity < b.specificity;
            if (a.sheet.order != b.sheet.order) return a.sheet.order < b.sheet.order;
            return a.index < b.index;
        }

        private static void SortRules(List<StyleRule> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                StyleRule item = list[i];
                int j = i - 1;
                while (j >= 0 && Before(item, list[j]))
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = item;
            }
        }

        private static void SortByDepth(List<Element> list)
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }
            int[] depths = new int[count];
            for (int i = 0; i < count; i++)
            {
                int depth = 0;
                for (Element e = list[i].parent; e != null; e = e.parent) depth++;
                depths[i] = depth;
            }
            for (int i = 1; i < count; i++)
            {
                Element item = list[i];
                int depth = depths[i];
                int j = i - 1;
                while (j >= 0 && depths[j] > depth)
                {
                    list[j + 1] = list[j];
                    depths[j + 1] = depths[j];
                    j--;
                }
                list[j + 1] = item;
                depths[j + 1] = depth;
            }
        }

        private readonly ComputedStyle enteringScratch = new ComputedStyle();

        /// Defaults, inherited values, matched rules (those whose states are
        /// all in `states`), the inline style, then the state variants.
        private void Cascade(Element element, ComputedStyle parent, ComputedStyle computed, int states, float scale)
        {
            computed.Reset(defaults);
            if (parent != null)
            {
                computed.InheritFrom(parent);
            }
            List<StyleRule> rules = element.matchedRules;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    StyleRule rule = rules[i];
                    if ((rule.States & ~states) == 0)
                    {
                        computed.Apply(rule.style, scale, currentContext);
                    }
                }
            }
            computed.Apply(element.inlineStyle, scale, currentContext);
            if ((states & PseudoState.Hover) != 0 && element.hoverStyle != null) computed.Apply(element.hoverStyle, scale, currentContext);
            if ((states & PseudoState.Focus) != 0 && element.focusStyle != null) computed.Apply(element.focusStyle, scale, currentContext);
            if ((states & PseudoState.Active) != 0 && element.activeStyle != null) computed.Apply(element.activeStyle, scale, currentContext);
            if ((states & PseudoState.DragOver) != 0 && element.dragOverStyle != null) computed.Apply(element.dragOverStyle, scale, currentContext);
            if ((states & PseudoState.Dragging) != 0 && element.draggingStyle != null) computed.Apply(element.draggingStyle, scale, currentContext);
            if ((states & PseudoState.Disabled) != 0 && element.disabledStyle != null) computed.Apply(element.disabledStyle, scale, currentContext);
            if ((states & PseudoState.EnterFrom) != 0 && element.enterFromStyle != null) computed.Apply(element.enterFromStyle, scale, currentContext);
            if ((states & PseudoState.ExitTo) != 0 && element.exitToStyle != null) computed.Apply(element.exitToStyle, scale, currentContext);
        }

        /// Returns whether an inherited value changed (children must follow).
        private bool Resolve(Element element, ComputedStyle parent, float scale)
        {
            ComputedStyle computed = element.computed;
            System.Array.Copy(computed.numbers, previousNumbers, StyleProperty.Count);
            System.Array.Copy(computed.units, previousUnits, StyleProperty.Count);
            System.Array.Copy(computed.colors, previousColors, StyleProperty.Count);

            int states = element.pseudoStates;
            Cascade(element, parent, computed, states, scale);
            // Transitions: the style above is the target; show where each
            // moving property is now. A new element starts from its
            // `:enter-from` style, if it has one.
            if (computed.hasTransitions || element.animator != null)
            {
                ComputedStyle entering = null;
                if (!element.styleResolvedOnce && (element.enterFromStyle != null
                    || (element.stateRuleMask & PseudoState.EnterFrom) != 0))
                {
                    entering = enteringScratch;
                    Cascade(element, parent, entering, states | PseudoState.EnterFrom, scale);
                }
                currentContext.styleAnimations.Apply(element, previousNumbers, previousUnits, previousColors, entering);
            }
            element.styleResolvedOnce = true;
            element.styleDirty = false;
            if (element.translateGroup != TransformGroups.Root || computed.IsTransformed
                || (element.animator != null && element.animator.IsRunning))
            {
                currentContext.UpdateLayer(element);
            }

            bool layoutChanged = previousNumbers[StyleProperty.FontFamily] != computed.numbers[StyleProperty.FontFamily];
            for (int i = 0; i < StyleProperty.FirstPaintOnly; i++)
            {
                if (previousNumbers[i] != computed.numbers[i] || previousUnits[i] != computed.units[i])
                {
                    layoutChanged = true;
                    break;
                }
            }
            if (layoutChanged)
            {
                element.MarkLayoutDirty(true);
            }
            if (previousNumbers[StyleProperty.Opacity] != computed.numbers[StyleProperty.Opacity]
                && element.translateGroup == TransformGroups.Root)
            {
                // Opacity is multiplied into every descendant's records (with
                // a transform group, the GPU applies it instead).
                element.MarkSubtreePaintDirty();
            }
            else
            {
                element.MarkPaintDirty();
            }
            // What pointers can hit (see InputRouter.RebuildHitList).
            if (previousNumbers[StyleProperty.PointerEvents] != computed.numbers[StyleProperty.PointerEvents]
                || previousNumbers[StyleProperty.Visibility] != computed.numbers[StyleProperty.Visibility]
                || (previousColors[StyleProperty.Background].a > 0f) != (computed.colors[StyleProperty.Background].a > 0f))
            {
                element.context.hitVersion++;
            }
            // What is drawn at all, and in which order: rebuild every record.
            if (previousNumbers[StyleProperty.Display] != computed.numbers[StyleProperty.Display]
                || previousNumbers[StyleProperty.ZIndex] != computed.numbers[StyleProperty.ZIndex]
                || previousUnits[StyleProperty.ZIndex] != computed.units[StyleProperty.ZIndex])
            {
                element.context.paintStructureDirty = true;
            }

            return previousNumbers[StyleProperty.FontSize] != computed.numbers[StyleProperty.FontSize]
                || previousNumbers[StyleProperty.TextAlign] != computed.numbers[StyleProperty.TextAlign]
                || previousColors[StyleProperty.TextColor] != computed.colors[StyleProperty.TextColor]
                || previousNumbers[StyleProperty.Visibility] != computed.numbers[StyleProperty.Visibility]
                || previousNumbers[StyleProperty.PointerEvents] != computed.numbers[StyleProperty.PointerEvents]
                || previousNumbers[StyleProperty.FontFamily] != computed.numbers[StyleProperty.FontFamily];
        }
    }
}
