using System;
using UnityEngine;

namespace UnityEngine
{
    public class AudioClip : Object
    {
    }
}

public class AudioElement : MonoBehaviour
{
    public AudioClip? clip;
    public float volume;
    public float pitch = 1f;
    public bool looping;
    public bool playing;
    public int PlayCalls { get; private set; }
    public int CancelFadeCalls { get; private set; }
    public float? FadeOutDestroyTime { get; private set; }

    public AudioElement Initialize() => this;
    public AudioElement SetClip(AudioClip audioClip) { clip = audioClip; return this; }
    public AudioElement SetVolume(float value) { volume = value; return this; }
    public AudioElement SetLooping(bool value) { looping = value; return this; }
    public AudioElement SetPitch(float value) { pitch = value; return this; }
    public AudioElement VariatePitch(float amount = 0.1f) => this;
    public AudioElement SetDelay(float delay) => this;
    public AudioElement CancelFade() { CancelFadeCalls++; return this; }
    public AudioElement FadeOutDestroy(float time = 0.5f) { FadeOutDestroyTime = time; return this; }
    public AudioElement Play() { PlayCalls++; playing = true; return this; }
    public bool IsPlaying() => playing;
    public bool IsLooping() => looping;
}

public class SoundManager : MonoBehaviour
{
    public static SoundManager? instance;
    public AudioElement audioElement = new();
    public Func<AudioElement?> NextElement { get; set; } = () => new AudioElement();
    public int GetAudioElementCalls { get; private set; }

    public AudioElement? GetAudioElement()
    {
        GetAudioElementCalls++;
        return NextElement();
    }

    public static AudioElement Play(AudioClip audioClip, float volume = 1f) =>
        instance!.GetAudioElement()!.Initialize().SetClip(audioClip).SetVolume(volume)
            .SetLooping(false).VariatePitch().Play();

    public static AudioElement PlayLoop(AudioClip audioClip, float volume = 1f) =>
        instance!.GetAudioElement()!.Initialize().SetClip(audioClip).SetVolume(volume)
            .SetLooping(true).VariatePitch().Play();
}

public class AudioInstance
{
    public int PlayCalls { get; private set; }

    public AudioElement? Play()
    {
        PlayCalls++;
        return null;
    }
}

public class UIRenderGroupElement : MonoBehaviour
{
    private void OnDisable()
    {
    }
}

public class UIResearchItem : UIRenderGroupElement
{
    public int DestroyResearchSoundCalls { get; private set; }

    public void DestroyResearchSound() => DestroyResearchSoundCalls++;
}

public class UIBrewingStation : UIRenderGroupElement
{
    public int DestroyLoopingAudioCalls { get; private set; }

    public void DestroyLoopingAudio() => DestroyLoopingAudioCalls++;
}
