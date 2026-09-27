using System;
using System.Collections.Generic;

namespace MenUI
{
    /// A value that remembers who read it and tells them when it changes.
    ///
    /// `signal.Value` inside a binding lambda (`() => $"{count.Value}"`) makes
    /// the binding depend on it; `signal.Value = x` elsewhere updates every
    /// dependent binding. Writing an equal value does nothing.
    public sealed class Signal<T> : IReactiveSource
    {
        private readonly ReactiveRuntime runtime;
        private readonly List<Observer> observers = new List<Observer>();
        private T value;

        internal Signal(ReactiveRuntime runtime, T initial)
        {
            this.runtime = runtime;
            value = initial;
        }

        public T Value
        {
            get
            {
                runtime.Track(this, observers);
                return value;
            }
            set
            {
                if (EqualityComparer<T>.Default.Equals(this.value, value))
                {
                    return;
                }
                this.value = value;
                runtime.NotifyAll(observers);
            }
        }

        /// Reads without becoming a dependency.
        public T Peek()
        {
            return value;
        }

        public void Update(Func<T, T> change)
        {
            Value = change(value);
        }

        /// Tells dependents the value changed although it is the same object
        /// (a list mutated in place, for example).
        public void NotifyChanged()
        {
            runtime.NotifyAll(observers);
        }

        public void Unsubscribe(Observer observer)
        {
            observers.Remove(observer);
        }
    }

    /// A value derived from other reactive values, recomputed lazily when one
    /// of them changed and it is read again.
    public sealed class Computed<T> : Observer, IReactiveSource
    {
        private readonly Func<T> compute;
        private readonly List<Observer> observers = new List<Observer>();
        private T value;
        private bool stale = true;

        internal Computed(ReactiveRuntime runtime, Owner parent, Func<T> compute) : base(runtime, parent)
        {
            this.compute = compute;
        }

        public T Value
        {
            get
            {
                runtime.Track(this, observers);
                if (stale && !disposed)
                {
                    Recompute();
                }
                return value;
            }
        }

        public T Peek()
        {
            if (stale && !disposed)
            {
                Recompute();
            }
            return value;
        }

        private void Recompute()
        {
            DisposeOwned();
            ClearSources();
            Observer savedObserver = runtime.currentObserver;
            Owner savedOwner = runtime.currentOwner;
            runtime.currentObserver = this;
            runtime.currentOwner = this;
            try
            {
                value = compute();
            }
            finally
            {
                runtime.currentObserver = savedObserver;
                runtime.currentOwner = savedOwner;
            }
            stale = false;
        }

        internal override void Notify()
        {
            if (stale)
            {
                return;
            }
            stale = true;
            runtime.NotifyAll(observers);
        }

        public void Unsubscribe(Observer observer)
        {
            observers.Remove(observer);
        }
    }

    /// Runs a function now and again whenever something it read changes.
    public sealed class Effect : Observer
    {
        private readonly Action body;
        internal bool queued;

        internal Effect(ReactiveRuntime runtime, Owner parent, Action body) : base(runtime, parent)
        {
            this.body = body;
        }

        internal void Run()
        {
            DisposeOwned();
            ClearSources();
            Observer savedObserver = runtime.currentObserver;
            Owner savedOwner = runtime.currentOwner;
            runtime.currentObserver = this;
            runtime.currentOwner = this;
            try
            {
                body();
            }
            finally
            {
                runtime.currentObserver = savedObserver;
                runtime.currentOwner = savedOwner;
            }
        }

        internal override void Notify()
        {
            runtime.Schedule(this);
        }
    }
}
