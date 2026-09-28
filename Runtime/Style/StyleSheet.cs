// Style sheets: CSS-like rules matched against elements' tags and classes.
//
//   StyleSheet sheet = new StyleSheet()
//       .Rule(".card", s => s.Padding(16).BorderRadius(12).Background(surface))
//       .Rule(".button", s => s.Padding(8, 16).Background(accent))
//       .Rule(".button:hover", s => s.Background(accentHover))
//       .Rule(".button.danger", s => s.Background(red))
//       .Rule(".card .title, .dialog > .title", s => s.FontSize(20));
//   AddStyleSheet(sheet);
//   Div().Class("card").Children(Text("Hi").Class("title"));
//
// Supported: tag (`text`, `div`, `scroll-view`, or any `.Tag(...)`), `*`,
// `.class`, compounds (`.a.b`), `:hover` `:active` `:focus` `:disabled`
// (and MenUI's `:dragging` `:drag-over`) on
// the element itself, descendant (` `) and child (`>`) combinators, commas.
// A pseudo-class on an ancestor (`.card:hover .title`) is not supported.
//
// Cascade: sheet rules by specificity (classes and pseudo-classes, then
// tags), later rules winning ties; then the element's own chained style;
// then its own state variants (`.Hover(...)`). Rules are matched when an
// element is attached or its classes change — never per frame — and a
// state change only switches rules already matched.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    public sealed class StyleSheet
    {
        internal readonly List<StyleRule> rules = new List<StyleRule>();
        // Candidate rules by the subject's first class or tag, or universal.
        internal readonly Dictionary<string, List<StyleRule>> byClass = new Dictionary<string, List<StyleRule>>();
        internal readonly Dictionary<string, List<StyleRule>> byTag = new Dictionary<string, List<StyleRule>>();
        internal readonly List<StyleRule> universal = new List<StyleRule>();
        /// Classes that appear left of the subject: changing one of these on
        /// an element can change what its descendants match.
        internal readonly HashSet<string> ancestorClasses = new HashSet<string>();
        internal bool hasAncestorTags;
        internal int order;   // assigned by the context, for ties across sheets

        /// Adds a rule; `selectors` may list several, separated by commas.
        public StyleSheet Rule(string selectors, Action<Style> configure)
        {
            Style style = new Style();
            configure(style);
            string[] parts = selectors.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                StyleRule rule = SelectorParser.Parse(parts[i].Trim(), style, rules.Count);
                if (rule == null)
                {
                    continue;
                }
                rule.sheet = this;
                rules.Add(rule);
                Index(rule);
            }
            return this;
        }

        /// Removes every rule (elements keep what they matched until matched again).
        internal void Clear()
        {
            rules.Clear();
            byClass.Clear();
            byTag.Clear();
            universal.Clear();
            ancestorClasses.Clear();
            hasAncestorTags = false;
        }

        private void Index(StyleRule rule)
        {
            Compound subject = rule.compounds[rule.compounds.Length - 1];
            if (subject.classes.Length > 0)
            {
                Bucket(byClass, subject.classes[0]).Add(rule);
            }
            else if (subject.tag != null)
            {
                Bucket(byTag, subject.tag).Add(rule);
            }
            else
            {
                universal.Add(rule);
            }
            for (int i = 0; i < rule.compounds.Length - 1; i++)
            {
                Compound ancestor = rule.compounds[i];
                for (int k = 0; k < ancestor.classes.Length; k++)
                {
                    ancestorClasses.Add(ancestor.classes[k]);
                }
                if (ancestor.tag != null)
                {
                    hasAncestorTags = true;
                }
            }
        }

        private static List<StyleRule> Bucket(Dictionary<string, List<StyleRule>> map, string key)
        {
            List<StyleRule> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<StyleRule>();
                map[key] = list;
            }
            return list;
        }
    }

    /// One simple-selector sequence: `tag.class1.class2:hover`.
    internal sealed class Compound
    {
        internal string tag;          // null: any
        internal string[] classes;
        internal int states;          // PseudoState bits required
    }

    internal sealed class StyleRule
    {
        internal StyleSheet sheet;
        internal Compound[] compounds;   // left to right
        internal bool[] childCombinator; // between compounds[i] and compounds[i + 1]
        internal Style style;
        internal int specificity;
        internal int index;              // position in its sheet
        internal int States { get { return compounds[compounds.Length - 1].states; } }

        /// Whether `e` matches, ignoring the subject's pseudo-classes (those
        /// are checked when resolving, against the element's current state).
        internal bool Matches(Element e)
        {
            return MatchFrom(e, compounds.Length - 1);
        }

        private bool MatchFrom(Element e, int position)
        {
            if (!CompoundMatches(compounds[position], e))
            {
                return false;
            }
            if (position == 0)
            {
                return true;
            }
            if (childCombinator[position - 1])
            {
                Element parent = StructuralParent(e);
                return parent != null && MatchFrom(parent, position - 1);
            }
            for (Element ancestor = StructuralParent(e); ancestor != null; ancestor = StructuralParent(ancestor))
            {
                if (MatchFrom(ancestor, position - 1))
                {
                    return true;
                }
            }
            return false;
        }

        /// The parent, looking through regions (Show/For), which exist in the
        /// tree but not in the markup a selector describes.
        private static Element StructuralParent(Element e)
        {
            Element parent = e.parent;
            while (parent != null && parent is Region)
            {
                parent = parent.parent;
            }
            return parent;
        }

        private static bool CompoundMatches(Compound compound, Element e)
        {
            if (compound.tag != null && compound.tag != e.tag)
            {
                return false;
            }
            string[] classes = compound.classes;
            for (int i = 0; i < classes.Length; i++)
            {
                if (!e.HasClass(classes[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal static class SelectorParser
    {
        internal static StyleRule Parse(string text, Style style, int index)
        {
            List<Compound> compounds = new List<Compound>();
            List<bool> combinators = new List<bool>();
            int position = 0;
            int length = text.Length;
            bool pendingChild = false;
            while (position < length)
            {
                char c = text[position];
                if (c == ' ')
                {
                    position++;
                    continue;
                }
                if (c == '>')
                {
                    pendingChild = true;
                    position++;
                    continue;
                }
                if (compounds.Count > 0)
                {
                    combinators.Add(pendingChild);
                }
                pendingChild = false;
                Compound compound = ParseCompound(text, ref position);
                if (compound == null)
                {
                    Debug.LogWarning("[MenUI] style sheet: cannot read the selector \"" + text + "\"; the rule is ignored.");
                    return null;
                }
                compounds.Add(compound);
            }
            if (compounds.Count == 0 || pendingChild)
            {
                Debug.LogWarning("[MenUI] style sheet: empty or incomplete selector \"" + text + "\"; the rule is ignored.");
                return null;
            }
            int classCount = 0;
            int tagCount = 0;
            for (int i = 0; i < compounds.Count; i++)
            {
                Compound compound = compounds[i];
                if (i < compounds.Count - 1 && compound.states != 0)
                {
                    Debug.LogWarning("[MenUI] style sheet: a pseudo-class on an ancestor (\"" + text
                        + "\") is not supported; the rule is ignored.");
                    return null;
                }
                classCount += compound.classes.Length + BitCount(compound.states);
                if (compound.tag != null)
                {
                    tagCount++;
                }
            }
            StyleRule rule = new StyleRule();
            rule.compounds = compounds.ToArray();
            rule.childCombinator = combinators.ToArray();
            rule.style = style;
            rule.specificity = classCount * 1000 + tagCount;
            rule.index = index;
            return rule;
        }

        private static Compound ParseCompound(string text, ref int position)
        {
            Compound compound = new Compound();
            List<string> classes = new List<string>();
            int length = text.Length;
            bool any = false;
            if (position < length && text[position] == '*')
            {
                position++;
                any = true;
            }
            else if (position < length && IsNameCharacter(text[position]))
            {
                compound.tag = ReadName(text, ref position);
                any = true;
            }
            while (position < length)
            {
                char c = text[position];
                if (c == '.')
                {
                    position++;
                    string name = ReadName(text, ref position);
                    if (name.Length == 0) return null;
                    classes.Add(name);
                    any = true;
                }
                else if (c == ':')
                {
                    position++;
                    string name = ReadName(text, ref position);
                    int state = name == "hover" ? PseudoState.Hover
                        : name == "active" ? PseudoState.Active
                        : name == "focus" ? PseudoState.Focus
                        : name == "disabled" ? PseudoState.Disabled
                        : name == "dragging" ? PseudoState.Dragging
                        : name == "drag-over" ? PseudoState.DragOver
                        : 0;
                    if (state == 0) return null;
                    compound.states |= state;
                    any = true;
                }
                else
                {
                    break;
                }
            }
            if (!any)
            {
                return null;
            }
            compound.classes = classes.ToArray();
            return compound;
        }

        private static string ReadName(string text, ref int position)
        {
            int start = position;
            while (position < text.Length && IsNameCharacter(text[position]))
            {
                position++;
            }
            return text.Substring(start, position - start);
        }

        private static bool IsNameCharacter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
        }

        private static int BitCount(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }
    }
}
