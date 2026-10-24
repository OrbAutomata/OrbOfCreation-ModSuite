using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class IdScriptableObject : UnityEngine.ScriptableObject
{
    public static Dictionary<Guid, IdScriptableObject> RuntimeLookup = new();
    public Guid uuid = Guid.NewGuid();
    public static object? GetInstance(Guid guid) => RuntimeLookup.TryGetValue(guid, out var value) ? value : null;
    public Exception? GetGuidFailure { get; set; }
    public Guid GetGuid() => GetGuidFailure is { } failure ? throw failure : uuid;
    public Guid GetId() => uuid;
    public void SetGuid(Guid guid) => uuid = guid;
}
