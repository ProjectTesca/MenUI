using System;
using System.Collections.Generic;

namespace MenUI
{
    /// Anything that can be read reactively.
    public interface IReactiveSource
    {
        void Unsubscribe(Observer observer);
    }

    /// Owns the effects and cleanups created while it was current. Disposing
    /// an owner disposes everything it owns, so when a conditional region
    /// switches branch, the bindings of the old branch stop with it.
    public class Owner
    {
        internal readonly ReactiveRuntime runtime;
        internal Owner parent;
        internal List<Owner> children;
        internal List<Action> cleanups;
        internal bool disposed;

        internal Owner(ReactiveRuntime runtime, Owner parent)
        {
            this.runtime = runtime;
            this.parent = parent;
            if (parent != null)
            {
                if (parent.children == null)
                {
                    parent.children = new List<Owner>();
                }
                parent.children.Add(this);
            }
        }

        public void OnCleanup(Action cleanup)
        {
            if (cleanups == null)
            {
                cleanups = new List<Action>();
            }
            cleanups.Add(cleanup);
        }

        /// Disposes what this owner owns, keeping the owner itself usable.
        internal void DisposeOwned()
        {
            if (children != null)
            {
                Owner[] owned = children.ToArray();
                children.Clear();
                for (int i = 0; i < owned.Length; i++)
                {
                    owned[i].parent = null;
                    owned[i].Dispose();
                }
            }
            if (cleanups != null)
            {
                Action[] pending = cleanups.ToArray();
                cleanups.Clear();
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i]();
                }
            }
        }

        public virtual void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DisposeOwned();
            if (parent != null && parent.children != null)
            {
                parent.children.Remove(this);
                parent = null;
            }
        }
    }

    /// An owner that reads sources and reacts when they change.
    public abstract class Observer : Owner
    {
        internal readonly List<IReactiveSource> sources = new List<IReactiveSource>();

        internal Observer(ReactiveRuntime runtime, Owner parent) : base(runtime, parent)
        {
        }

        /// A source this observer read has changed.
        internal abstract void Notify();

        internal void ClearSources()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Unsubscribe(this);
            }
            sources.Clear();
        }

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }
            ClearSources();
            base.Dispose();
        }
    }
}
