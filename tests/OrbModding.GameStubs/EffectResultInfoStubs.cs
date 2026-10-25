using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class EffectResultInfo
{
    public static bool SuppressCancel { get; set; }
    public static bool ThrowAfterCancel { get; set; }
    private bool cancelled;
    internal readonly List<TargetingManager.TargetLink> Links = new();
    public bool IsCancelled() => cancelled;
    public void Cancel()
    {
        if (SuppressCancel) return;
        cancelled = true;
        foreach (var link in Links.ToArray()) TargetingManager.RemoveRequest(link);
        if (ThrowAfterCancel) throw new InvalidOperationException("stub throw after cancel");
    }
}
