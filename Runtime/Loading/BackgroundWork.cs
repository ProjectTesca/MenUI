// Background loading: content built a little every frame, out of sight,
// shown when it is ready (see MenUIContext.Deferred, Pages, Near).
//
//   Deferred(async load =>
//   {
//       Box list = Div().Column();
//       load.Content.Child(list);           // attach early: each slice is styled
//       for (int i = 0; i < 500; i++)       // and laid out in its own frame
//       {
//           list.Child(Row(i));
//           load.Progress = i / 500f;
//           await load.Yield();             // goes on at once while the frame's budget lasts
//       }
//   }, placeholder: () => Spinner());
//
// Each frame, after MenUI's own work, the waiting loads get what is left of
// the budget (UI.BackgroundBudget, ms): a load runs step after step (a step
// ends at its next Yield) until the time is used up. The first step of a
// frame always runs, however busy the frame was, so loading never stalls.
// What a load builds belongs to its scope (bindings stop when it is
// removed); a load whose place is removed stops at its next Yield.
//
// Steps are resumed at the end of MenUI's frame; under MenSharp the step
// itself runs just after, when the event's queued continuations run.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MenUI
{
    /// What an async Deferred builder gets: where to put the content, a way
    /// to hand back the rest of the frame, and progress for the placeholder.
    public sealed class DeferredLoad
    {
        internal readonly BackgroundWork work;
        internal readonly Owner owner;
        private readonly Signal<float> progress;
        internal TaskCompletionSource<bool> waiting;
        internal Owner savedOwner;
        internal Observer savedObserver;
        internal bool cancelled;
        internal bool finished;
        internal Action onFinished;
        /// When its content was last laid out (context time).
        internal float laidOutAt;
        /// The frame it finished in (it is shown a frame later).
        internal int finishedFrame;

        internal DeferredLoad(BackgroundWork work, Owner owner, Box content)
        {
            this.work = work;
            this.owner = owner;
            Content = content;
            progress = new Signal<float>(work.context.runtime, 0f);
        }

        /// The (not yet visible) box to fill; shown when the builder returns.
        public Box Content { get; private set; }

        /// 0..1, for the placeholder (a dependency when read in a binding).
        public float Progress
        {
            get { return progress.Value; }
            set { progress.Value = Mathf.Clamp01(value); }
        }

        /// The load's place was removed: it stops at its next Yield.
        public bool IsCancelled { get { return cancelled; } }

        /// Goes on at once while this frame's budget lasts; otherwise in a
        /// later frame.
        public Task Yield()
        {
            return work.Yield(this, false);
        }

        /// Goes on in a later frame, whatever the budget.
        public Task NextFrame()
        {
            return work.Yield(this, true);
        }
    }

    internal sealed class BackgroundWork
    {
        internal readonly MenUIContext context;
        /// Milliseconds a frame may spend on background loading (MenUI's
        /// own work that frame counts against it).
        internal float budget = 1.5f;
        private readonly List<DeferredLoad> queue = new List<DeferredLoad>();
        // Every unfinished load (waiting or running).
        private readonly List<DeferredLoad> loads = new List<DeferredLoad>();
        // Finished loads waiting to be shown: first their last additions are
        // laid out and drawn (at zero opacity) in a frame of their own.
        private readonly List<DeferredLoad> finishing = new List<DeferredLoad>();
        /// Content being loaded is laid out this often (seconds), not every
        /// frame: laying out a growing list each frame would cost its whole
        /// length every frame.
        private const float LayoutInterval = 0.25f;
        private long sliceStart;
        private double allowance;
        private bool slicing;
        private DeferredLoad running;

        internal BackgroundWork(MenUIContext context)
        {
            this.context = context;
        }

        internal bool Busy { get { return queue.Count > 0 || running != null; } }

        /// Starts `build` as a load: its first step runs in a coming slice.
        internal DeferredLoad Start(Owner owner, Box content, Func<DeferredLoad, Task> build, Action onFinished)
        {
            DeferredLoad load = new DeferredLoad(this, owner, content);
            load.onFinished = onFinished;
            // Out of sight while it loads: what it starts waits until shown.
            owner.suspended = true;
            load.laidOutAt = context.time;
            content.layoutPaused = true;
            content.loading = true;
            loads.Add(load);
            load.waiting = new TaskCompletionSource<bool>();
            owner.OnCleanup(() => Cancel(load));
            Task started = load.waiting.Task;
            Func<Task> runner = async () =>
            {
                bool ok = false;
                try
                {
                    await started;
                    await build(load);
                    ok = true;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    Debug.LogError("[MenUI] a deferred load failed: " + exception);
                }
                Finish(load, ok);
            };
            runner();
            queue.Add(load);
            return load;
        }

        /// Runs the waiting loads for what is left of this frame's budget
        /// after `spentMilliseconds` of MenUI's own work (at least one step).
        internal void RunSlice(double spentMilliseconds)
        {
            ShowFinished();
            PaceLayout();
            if (queue.Count == 0 || running != null)
            {
                return;
            }
            sliceStart = Stopwatch.GetTimestamp();
            allowance = budget - spentMilliseconds;
            slicing = true;
            Resume(queue[0]);
        }

        /// Shows the loads that finished before this frame (whose content
        /// this frame laid out and drew, out of sight).
        private void ShowFinished()
        {
            for (int i = finishing.Count - 1; i >= 0; i--)
            {
                DeferredLoad load = finishing[i];
                if (load.finishedFrame >= context.frame)
                {
                    continue;
                }
                finishing.RemoveAt(i);
                load.Content.loading = false;
                if (!load.cancelled && load.onFinished != null)
                {
                    context.runtime.Batch(load.onFinished);
                }
            }
        }

        /// Lets each load's content be laid out in the coming frame now and
        /// then (and pauses it again after).
        private void PaceLayout()
        {
            for (int i = 0; i < loads.Count; i++)
            {
                DeferredLoad load = loads[i];
                Box content = load.Content;
                if (!content.layoutPaused)
                {
                    content.layoutPaused = true;   // it was laid out this frame
                    if (content.parent != null)
                    {
                        content.parent.TruncateStack(content);   // out of the flow again
                    }
                }
                else if (context.time - load.laidOutAt >= LayoutInterval)
                {
                    load.laidOutAt = context.time;
                    ResumeLayout(content);
                }
            }
        }

        /// Lays the content out in the coming frame, and makes the records
        /// of what was added (a prepared box is drawn at zero opacity), so
        /// showing it later costs little.
        private void ResumeLayout(Box content)
        {
            content.layoutPaused = false;
            content.MarkLayoutDirty(true);
            Element owner = content.RangeOwner();
            if (owner != null)
            {
                context.RequestRangePaint(owner);   // only its own records
            }
        }

        private void Resume(DeferredLoad load)
        {
            queue.Remove(load);
            ReactiveRuntime runtime = context.runtime;
            load.savedOwner = runtime.currentOwner;
            load.savedObserver = runtime.currentObserver;
            runtime.currentOwner = load.owner;
            runtime.currentObserver = null;
            running = load;
            TaskCompletionSource<bool> resume = load.waiting;
            load.waiting = null;
            resume.TrySetResult(true);
        }

        /// The builder hands back control: go on now (time left), or wait
        /// for a later slice.
        internal Task Yield(DeferredLoad load, bool nextFrame)
        {
            if (load.cancelled)
            {
                Restore(load);
                throw new OperationCanceledException();
            }
            if (!nextFrame && slicing && Milliseconds(Stopwatch.GetTimestamp() - sliceStart) < allowance)
            {
                return Task.CompletedTask;
            }
            load.waiting = new TaskCompletionSource<bool>();
            queue.Add(load);
            Restore(load);
            slicing = false;
            return load.waiting.Task;
        }

        private void Restore(DeferredLoad load)
        {
            if (running != load)
            {
                return;
            }
            ReactiveRuntime runtime = context.runtime;
            runtime.currentOwner = load.savedOwner;
            runtime.currentObserver = load.savedObserver;
            running = null;
        }

        private void Finish(DeferredLoad load, bool ok)
        {
            Restore(load);
            queue.Remove(load);
            loads.Remove(load);
            load.finished = true;
            ResumeLayout(load.Content);
            if (ok && !load.cancelled)
            {
                // Still counted as coming (scrollbars keep their room) until shown.
                load.finishedFrame = context.frame;
                finishing.Add(load);
            }
            else
            {
                load.Content.loading = false;
            }
            // Time left: the next load goes on in this slice.
            if (slicing && queue.Count > 0 && Milliseconds(Stopwatch.GetTimestamp() - sliceStart) < allowance)
            {
                Resume(queue[0]);
            }
            else
            {
                slicing = false;
            }
        }

        private void Cancel(DeferredLoad load)
        {
            if (load.finished || load.cancelled)
            {
                return;
            }
            load.cancelled = true;
            queue.Remove(load);
            loads.Remove(load);
            load.Content.loading = false;
            if (load.waiting != null)
            {
                TaskCompletionSource<bool> waiting = load.waiting;
                load.waiting = null;
                waiting.TrySetException(new OperationCanceledException());
            }
        }

        private static double Milliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
