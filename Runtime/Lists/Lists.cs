// Lists.
//
// For: a keyed list. Each key keeps its element (and the bindings inside)
// while it stays in the list — it is moved, not rebuilt; a new key builds an
// element; a key that left has its element's bindings disposed. The builder
// gets the item and its index as signals, so an item replaced under the same
// key (a record changed with `with`) updates in place. A removed element
// with an `:exit-to` style stays where it was until its exit has played.
//
//   For(() => todos.Value, t => t.Id, (item, index) =>
//       Text(() => $"{index.Value + 1}. {item.Value.Title}"))
//
// VirtualList: a scrolling list that only has elements for the rows in view
// (plus a few more), recycling them as rows scroll in and out. Rows have a
// fixed height. Each row slot is positioned on the GPU (a translate group),
// so scrolling — including rows changing slot — never lays out the list;
// only a slot that receives a new row updates its own content.
//
//   VirtualList(scroll, () => items, 36, (item, index) => Row(item, index))

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    internal sealed class ListEntry<T>
    {
        internal Owner owner;
        internal Signal<T> item;
        internal Signal<int> index;
        internal Element element;
        internal int seenStamp;
    }

    internal sealed class KeyedList<T, K>
    {
        private readonly MenUIContext context;
        private readonly Region region;
        private readonly Owner scope;
        private readonly Func<List<T>> items;
        private readonly Func<T, K> key;
        private readonly Func<Signal<T>, Signal<int>, Element> render;
        private readonly Dictionary<K, ListEntry<T>> entries = new Dictionary<K, ListEntry<T>>();
        private readonly List<Element> order = new List<Element>();
        private readonly List<K> gone = new List<K>();
        // Removed entries still playing their exit.
        private readonly List<ListEntry<T>> exiting = new List<ListEntry<T>>();
        private int stamp;

        internal KeyedList(MenUIContext context, Region region, Owner scope, Func<List<T>> items, Func<T, K> key,
            Func<Signal<T>, Signal<int>, Element> render)
        {
            this.context = context;
            this.region = region;
            this.scope = scope;
            this.items = items;
            this.key = key;
            this.render = render;
        }

        internal void Reconcile()
        {
            List<T> list = items();   // the only tracked read: the list itself
            ReactiveRuntime runtime = context.runtime;
            stamp++;
            order.Clear();
            int count = list != null ? list.Count : 0;
            for (int i = 0; i < count; i++)
            {
                T value = list[i];
                K k = key(value);
                ListEntry<T> entry;
                if (entries.TryGetValue(k, out entry))
                {
                    if (entry.seenStamp == stamp)
                    {
                        Debug.LogWarning("[MenUI] For: duplicate key " + k + "; only its first item is shown.");
                        continue;
                    }
                    entry.item.Value = value;
                    entry.index.Value = i;
                }
                else
                {
                    entry = new ListEntry<T>();
                    entry.owner = new Owner(runtime, scope);
                    entry.item = new Signal<T>(runtime, value);
                    entry.index = new Signal<int>(runtime, i);
                    ListEntry<T> building = entry;
                    runtime.RunWithOwner(entry.owner, () => { building.element = render(building.item, building.index); });
                    entries[k] = entry;
                }
                entry.seenStamp = stamp;
                if (entry.element != null)
                {
                    order.Add(entry.element);
                }
            }

            gone.Clear();
            foreach (KeyValuePair<K, ListEntry<T>> pair in entries)
            {
                if (pair.Value.seenStamp != stamp)
                {
                    gone.Add(pair.Key);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                ListEntry<T> leaving = entries[gone[i]];
                entries.Remove(gone[i]);
                if (leaving.element == null)
                {
                    leaving.owner.Dispose();
                    continue;
                }
                if (context.Leave(leaving.owner, () =>
                    {
                        exiting.Remove(leaving);
                        region.RemoveChild(leaving.element);
                    }))
                {
                    exiting.Add(leaving);
                }
            }
            // Exiting elements keep their place among the others (placed in
            // the order they are shown, so earlier indices stay right).
            List<Element> children = region.children;
            for (int i = 0; i < children.Count; i++)
            {
                Element child = children[i];
                for (int j = 0; j < exiting.Count; j++)
                {
                    if (exiting[j].element == child)
                    {
                        order.Insert(Mathf.Min(i, order.Count), child);
                        break;
                    }
                }
            }
            region.ReplaceChildren(order);
        }
    }

    internal sealed class VirtualSlot<T>
    {
        internal Box container;
        internal Signal<T> item;
        internal Signal<int> index;
        internal Signal<float> y;
        /// The row this slot should show (-1: none).
        internal int targetIndex = -1;
        /// The row whose content the slot holds (-1: none). The slot is
        /// visible only when both agree, so it never shows stale content.
        internal int shownIndex = -1;
        internal bool pending;
    }

    internal sealed class VirtualListController<T> : IFrameTask
    {
        private const float HiddenY = -100000f;

        private readonly MenUIContext context;
        private readonly ScrollState state;
        private readonly Func<List<T>> items;
        private readonly float itemHeight;
        private readonly float pitch;
        private readonly int overscan;
        private readonly int rowsPerFrame;
        private readonly Func<Signal<T>, Signal<int>, Element> render;
        private readonly Owner scope;
        internal readonly Box content;
        internal readonly List<VirtualSlot<T>> slots = new List<VirtualSlot<T>>();
        private readonly List<VirtualSlot<T>> free = new List<VirtualSlot<T>>();
        private readonly List<VirtualSlot<T>> pending = new List<VirtualSlot<T>>();
        private bool ticking;

        private int shownFirst = -1;
        private int shownLast = -2;
        private int visibleFirst;
        private int visibleLast;
        private List<T> shownList;
        private int shownCount = -1;

        /// Rows filled in the current frame (the budget), and which frame.
        internal int filledThisFrame;
        private int budgetFrame = -1;

        internal VirtualListController(MenUIContext context, ScrollState state, Func<List<T>> items, float itemHeight,
            float gap, int overscan, int rowsPerFrame, Func<Signal<T>, Signal<int>, Element> render)
        {
            this.context = context;
            this.state = state;
            this.items = items;
            this.itemHeight = itemHeight;
            pitch = itemHeight + gap;
            this.overscan = overscan;
            this.rowsPerFrame = Mathf.Max(1, rowsPerFrame);
            this.render = render;
            scope = context.runtime.currentOwner;
            content = context.Div().Position(PositionMode.Relative)
                .Height(() => Length.Pixels(Mathf.Max(0f, CountOf(items()) * pitch - (pitch - itemHeight))));
        }

        private static int CountOf(List<T> list)
        {
            return list != null ? list.Count : 0;
        }

        /// Runs whenever the list, the offset or the viewport changes; does
        /// work only when the range of rows to show (or the data) changed.
        internal void Update()
        {
            List<T> list = items();
            int count = CountOf(list);
            float offset = state.OffsetY;
            float viewport = Mathf.Max(state.ViewportHeight, pitch);

            // Rows with any part in view, then the overscan around them.
            visibleFirst = Mathf.Max(0, Mathf.FloorToInt(offset / pitch));
            visibleLast = Mathf.Min(count - 1, Mathf.FloorToInt((offset + viewport - 0.001f) / pitch));
            int first = Mathf.Max(0, visibleFirst - overscan);
            int last = Mathf.Min(count - 1, visibleLast + overscan);
            bool dataChanged = list != shownList || count != shownCount;
            if (!dataChanged && first == shownFirst && last == shownLast)
            {
                return;
            }
            shownList = list;
            shownCount = count;
            shownFirst = first;
            shownLast = last;

            // Enough slots for any scroll position, made once, so scrolling
            // never has to build (and lay out) new ones.
            int steadyState = Mathf.Min(count, Mathf.CeilToInt(viewport / pitch) + 1 + 2 * overscan);
            context.Batch(() => Assign(list, first, last, steadyState, dataChanged));
        }

        private void Assign(List<T> list, int first, int last, int steadyState, bool dataChanged)
        {
            int span = Mathf.Max(0, last - first + 1);
            bool[] covered = new bool[span];
            free.Clear();
            for (int s = 0; s < slots.Count; s++)
            {
                VirtualSlot<T> slot = slots[s];
                int index = slot.targetIndex;
                if (index >= first && index <= last)
                {
                    covered[index - first] = true;
                    if (dataChanged && slot.shownIndex == index)
                    {
                        slot.item.Value = list[index];   // same item: no-op
                    }
                }
                else
                {
                    slot.targetIndex = -1;
                    free.Add(slot);
                }
            }

            int nextFree = 0;
            for (int k = 0; k < span; k++)
            {
                if (covered[k])
                {
                    continue;
                }
                int index = first + k;
                if (nextFree < free.Count)
                {
                    VirtualSlot<T> slot = free[nextFree++];
                    slot.targetIndex = index;
                    if (slot.shownIndex == index && !dataChanged)
                    {
                        slot.y.Value = index * pitch;   // still holds this row
                    }
                    else
                    {
                        slot.y.Value = HiddenY;         // filled within the frame budget
                        MarkPending(slot);
                    }
                }
                else
                {
                    CreateSlot(list[index], index);
                }
            }
            for (; nextFree < free.Count; nextFree++)
            {
                free[nextFree].y.Value = HiddenY;
            }
            while (slots.Count < steadyState)
            {
                CreateSlot(list[0], -1);   // a spare, hidden until needed
            }

            // Fill what fits in this frame now (rows in view first), the rest later.
            Fill(list);
            if (pending.Count > 0 && !ticking)
            {
                ticking = true;
                context.StartAnimating(this);
            }
        }

        private void MarkPending(VirtualSlot<T> slot)
        {
            if (!slot.pending)
            {
                slot.pending = true;
                pending.Add(slot);
            }
        }

        public bool Tick(float seconds)
        {
            context.Batch(() => Fill(shownList));
            ticking = pending.Count > 0;
            return ticking;
        }

        /// Gives up to `rowsPerFrame` pending slots their row, rows in view first.
        private void Fill(List<T> list)
        {
            if (budgetFrame != context.frame)
            {
                budgetFrame = context.frame;
                filledThisFrame = 0;
            }
            int budget = rowsPerFrame - filledThisFrame;
            for (int pass = 0; pass < 2 && budget > 0; pass++)
            {
                for (int i = 0; i < pending.Count && budget > 0; i++)
                {
                    VirtualSlot<T> slot = pending[i];
                    int index = slot.targetIndex;
                    bool inView = index >= visibleFirst && index <= visibleLast;
                    if (index >= 0 && pass == 0 && !inView)
                    {
                        continue;
                    }
                    pending.RemoveAt(i);
                    i--;
                    slot.pending = false;
                    if (index < 0 || list == null || index >= list.Count)
                    {
                        continue;   // no longer wanted
                    }
                    slot.item.Value = list[index];
                    slot.index.Value = index;
                    slot.shownIndex = index;
                    slot.y.Value = index * pitch;
                    budget--;
                    filledThisFrame++;
                    context.rowsFilled++;
                }
            }
        }

        private void CreateSlot(T value, int index)
        {
            ReactiveRuntime runtime = context.runtime;
            VirtualSlot<T> slot = new VirtualSlot<T>();
            slot.item = new Signal<T>(runtime, value);
            slot.index = new Signal<int>(runtime, index);
            slot.y = new Signal<float>(runtime, index >= 0 ? index * pitch : HiddenY);
            slot.targetIndex = index;
            slot.shownIndex = index;
            Element row = null;
            Owner owner = new Owner(runtime, scope);
            runtime.RunWithOwner(owner, () =>
            {
                row = render(slot.item, slot.index);
                // Absolute with both insets and a pixel height: a relayout
                // boundary, so a row's new content lays out only the row.
                slot.container = context.Div().Absolute().Left(0).Right(0).Top(0).Height(itemHeight)
                    .Translate(() => 0f, () => slot.y.Value);
                if (row != null)
                {
                    slot.container.Child(row);
                }
            });
            slots.Add(slot);
            content.Child(slot.container);
        }
    }

    public static class ListFactory
    {
        public static Region For<T, K>(MenUIContext context, Func<List<T>> items, Func<T, K> key,
            Func<Signal<T>, Signal<int>, Element> render)
        {
            Region region = new Region(context);
            KeyedList<T, K> list = new KeyedList<T, K>(context, region, context.runtime.currentOwner, items, key, render);
            context.Bind(() => list.Reconcile());
            return region;
        }

        public static ScrollView VirtualList<T>(MenUIContext context, ScrollState state, Func<List<T>> items, float itemHeight,
            Func<Signal<T>, Signal<int>, Element> render, float gap, int overscan, int rowsPerFrame)
        {
            VirtualListController<T> controller = new VirtualListController<T>(context, state, items, itemHeight, gap, overscan, rowsPerFrame, render);
            ScrollView view = context.ScrollView(state).Children(controller.content);
            context.Bind(() => controller.Update());
            return view;
        }
    }
}
