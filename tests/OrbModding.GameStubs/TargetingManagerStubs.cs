using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class TargetingManager
{
    /// <summary>
    /// How many target requests are open. The game opens one per targeted effect a cast triggers and
    /// closes one per target submitted, which is why resolving a cast is a loop and not a single call.
    /// </summary>
    public static int OpenRequests { get; set; }

    /// <summary>What the selector will offer, or null for a request nothing can satisfy.</summary>
    public static Targeting.ITargetable? AvailableTarget { get; set; }

    public static List<object> SubmittedTargets { get; } = new List<object>();
    public static bool SuppressSubmit { get; set; }
    public static bool ThrowAfterSubmit { get; set; }
    public static bool ThrowOnIsTargeting { get; set; }
    public static Exception? IsTargetingFault { get; set; }
    private static TargetLink? currentLink;
    public static TargetLink? Current
    {
        get => currentLink;
        set { currentLink = value; OpenRequests = value is null ? 0 : 1; }
    }

    public static bool Targeting
    {
        get => OpenRequests > 0;
        set => OpenRequests = value ? 1 : 0;
    }

    public static bool IsTargeting() =>
        IsTargetingFault is { } fault ? throw fault
        : ThrowOnIsTargeting ? throw new NullReferenceException("stub targeting read")
        : OpenRequests > 0;

    public static TargetLink? GetTargetingLink() =>
        !IsTargeting()
            ? null
            : currentLink ?? (AvailableTarget is null ? null : currentLink = new TargetLink(AvailableTarget));

    public static void SubmitTarget(Targeting.ITargetable target)
    {
        if (SuppressSubmit) return;
        var link = GetTargetingLink();
        link?.AssignTarget(target);
        SubmittedTargets.Add(target);
        if (OpenRequests > 0) OpenRequests--;
        currentLink = null;
        if (ThrowAfterSubmit) throw new InvalidOperationException("stub throw after submit");
    }

    public static void RemoveRequest(TargetLink link)
    {
        if (!ReferenceEquals(currentLink, link)) return;
        currentLink = null;
        if (OpenRequests > 0) OpenRequests--;
    }

    public class TargetLink
    {
        private readonly Targeting.ITargetable offered;
        private readonly List<ITooltipable>? offeredTargets;
        private readonly ITooltipable owner;
        private readonly Targeting.BaseTargetSelection targetSelection = new Targeting.BaseTargetSelection();
        private Targeting.ITargetable? target;
        private readonly EffectResultInfo resultInfo;
        private bool targetFound;

        public TargetLink(Targeting.ITargetable offeredTarget)
        {
            offered = offeredTarget;
            owner = offeredTarget as ITooltipable ?? new TooltipableObject { displayName = "Target request" };
            resultInfo = new EffectResultInfo();
            resultInfo.Links.Add(this);
        }

        public TargetLink(ITooltipable requestOwner, params ITooltipable[] targets)
        {
            owner = requestOwner;
            offeredTargets = new List<ITooltipable>(targets);
            offered = targets.OfType<Targeting.ITargetable>().FirstOrDefault()!;
            resultInfo = new EffectResultInfo();
            resultInfo.Links.Add(this);
        }
        public Targeting.ITargetable GetRandom() => offered;
        public List<ITooltipable> GetAllTargets() => offeredTargets is not null ? new List<ITooltipable>(offeredTargets) : offered is ITooltipable tooltipable
            ? new List<ITooltipable> { tooltipable }
            : new List<ITooltipable>();
        public ITooltipable GetOwner() => owner;
        public Targeting.BaseTargetSelection GetTargetSelection() => targetSelection;
        public bool CheckTarget(Targeting.ITargetable candidate) => ReferenceEquals(candidate, offered);
        public bool HasTarget() => targetFound;
        internal void AssignTarget(Targeting.ITargetable candidate)
        {
            target = candidate;
            targetFound = true;
        }
    }
}
