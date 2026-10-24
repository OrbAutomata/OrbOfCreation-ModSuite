using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class SaveStateManager
{
    public static SaveStateManager instance = new();

    public void ImplementLoadedJson()
    {
    }

    public void StartGame()
    {
    }

    public void BackToMainMenu()
    {
        UIScreenFlash.instance.FadeIn(0f, 0f);
    }
}
