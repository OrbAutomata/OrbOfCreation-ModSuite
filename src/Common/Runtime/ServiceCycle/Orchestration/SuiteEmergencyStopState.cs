using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// Owner-thread emergency-stop episode state, including the first causal episode latched for
/// the duration of a frame-pump call.
/// </summary>
internal sealed class SuiteEmergencyStopState
{
    private long _nextTransition;
    private long _nextEpisode;
    private EmergencyStopTransitionGeneration _transition;
    private EmergencyStopContext _active;
    private EmergencyStopContext _latest;
    private EmergencyStopContext _firstContextThisFrame;
    private bool _engaged;
    private bool _engagedThisFrame;

    internal bool IsEngaged => _engaged;
    internal EmergencyStopTransitionGeneration Transition => _transition;
    internal EmergencyStopContext Active => _active;
    internal EmergencyStopContext Latest => _latest;
    internal bool IsEffective => _engaged || _engagedThisFrame;

    internal EmergencyStopContext EffectiveContext =>
        _firstContextThisFrame.IsValid ? _firstContextThisFrame : _active;

    internal void Set(bool engaged, EmergencyStopReason reason, bool pumping)
    {
        if (engaged && !_engaged)
        {
            _engaged = true;
            _transition = new EmergencyStopTransitionGeneration(checked(++_nextTransition));
            _active = new EmergencyStopContext(
                new EmergencyStopEpisodeId(checked(++_nextEpisode)),
                _transition,
                reason);
            _latest = _active;
            if (pumping)
            {
                _engagedThisFrame = true;
                if (!_firstContextThisFrame.IsValid)
                    _firstContextThisFrame = _active;
            }
        }
        else if (!engaged && _engaged)
        {
            _engaged = false;
            _transition = new EmergencyStopTransitionGeneration(checked(++_nextTransition));
            _active = default;
        }
    }

    internal void BeginFrame()
    {
        _engagedThisFrame = _engaged;
        _firstContextThisFrame = _engaged ? _active : default;
    }

    internal void EndFrame()
    {
        _engagedThisFrame = false;
        _firstContextThisFrame = default;
    }
}
