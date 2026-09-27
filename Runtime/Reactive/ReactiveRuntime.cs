// The fine-grained reactive core (SolidJS-style).
//
// Reading a Signal while an Observer runs records a dependency; writing one
// notifies the observers that read it. Effects rerun synchronously when a
// write happens outside a batch, or once at the end of the batch.
//
// There is one runtime per panel and it is reached through instances, never
// through statics: MenSharp shares mutable statics between every behaviour of
// a world, and a delegate cannot cross from one behaviour to another.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    public sealed class ReactiveRuntime
    {
        private const int MaximumFlushRounds = 100;

        internal Observer currentObserver;
        internal Owner currentOwner;
        private int batchDepth;
        private bool flushing;
        private List<Effect> queue = new List<Effect>();
        private List<Effect> running = new List<Effect>();

        internal readonly Owner root;

        public ReactiveRuntime()
        {
            root = new Owner(this, null);
            currentOwner = root;
        }

        /// Runs `action` with every effect it triggers deferred to its end.
        public void Batch(Action action)
        {
            batchDepth++;
            try
            {
                action();
            }
            finally
            {
                batchDepth--;
                if (batchDepth == 0)
                {
                    Flush();
                }
            }
        }

        /// Reads without recording dependencies.
        public T Untrack<T>(Func<T> read)
        {
            Observer saved = currentObserver;
            currentObserver = null;
            try
            {
                return read();
            }
            finally
            {
                currentObserver = saved;
            }
        }

        /// Runs `action` with `owner` as the owner of whatever it creates.
        internal void RunWithOwner(Owner owner, Action action)
        {
            Owner savedOwner = currentOwner;
            Observer savedObserver = currentObserver;
            currentOwner = owner;
            currentObserver = null;
            try
            {
                action();
            }
            finally
            {
                currentOwner = savedOwner;
                currentObserver = savedObserver;
            }
        }

        internal void Track(IReactiveSource source, List<Observer> observers)
        {
            Observer observer = currentObserver;
            if (observer == null || observer.disposed)
            {
                return;
            }
            if (!observer.sources.Contains(source))
            {
                observer.sources.Add(source);
                observers.Add(observer);
            }
        }

        internal void NotifyAll(List<Observer> observers)
        {
            int count = observers.Count;
            if (count == 0)
            {
                return;
            }
            // Notifying can change the list (an effect rerunning resubscribes).
            Observer[] snapshot = observers.ToArray();
            batchDepth++;
            for (int i = 0; i < count; i++)
            {
                snapshot[i].Notify();
            }
            batchDepth--;
            if (batchDepth == 0)
            {
                Flush();
            }
        }

        internal void Schedule(Effect effect)
        {
            if (effect.queued || effect.disposed)
            {
                return;
            }
            effect.queued = true;
            queue.Add(effect);
        }

        private void Flush()
        {
            if (flushing)
            {
                return;
            }
            flushing = true;
            int rounds = 0;
            while (queue.Count > 0)
            {
                if (++rounds > MaximumFlushRounds)
                {
                    Debug.LogError("[MenUI] effects keep triggering each other; stopped after " + MaximumFlushRounds + " rounds.");
                    for (int i = 0; i < queue.Count; i++)
                    {
                        queue[i].queued = false;
                    }
                    queue.Clear();
                    break;
                }
                List<Effect> swap = running;
                running = queue;
                queue = swap;
                for (int i = 0; i < running.Count; i++)
                {
                    Effect effect = running[i];
                    effect.queued = false;
                    if (!effect.disposed)
                    {
                        effect.Run();
                    }
                }
                running.Clear();
            }
            flushing = false;
        }
    }
}
