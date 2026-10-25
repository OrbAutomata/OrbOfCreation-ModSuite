using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class UIScreenFlash : UnityEngine.MonoBehaviour
{
    public static UIScreenFlash instance = new();
    private bool isActive;

    public UIScreenFlash FadeIn(float delay, float duration)
    {
        if (!SuppressFade) isActive = true;
        return this;
    }

    public static bool SuppressFade { get; set; }
    public bool ActiveForTests => isActive;

}
