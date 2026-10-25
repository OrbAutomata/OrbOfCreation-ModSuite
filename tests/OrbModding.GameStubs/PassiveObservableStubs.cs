using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class PassiveObservable : IPassiveObservable
{
    private int _observedId;

    public int GetObservableId() => _observedId;
    public void UpdateObservable() => _observedId++;

    public sealed class Channel
    {
        private readonly Dictionary<string, PassiveObservable> _values = new();

        public Channel(params string[] names)
        {
            foreach (var name in names) _values[name] = new PassiveObservable();
        }

        public IPassiveObservable Observe(string name) => _values[name];
        public void Update(string name) => _values[name].UpdateObservable();
    }
}
