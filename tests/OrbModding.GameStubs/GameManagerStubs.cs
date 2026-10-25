using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class GameManager
{
    public static long currentFrame;
    public static int PersistentResetCalls { get; set; }
    public static int CleanGameCalls { get; set; }

    public static void ResetGameState()
    {
    }

    public static void PersistentResetGameState() => PersistentResetCalls++;

    public static void CleanGame() => CleanGameCalls++;

    public void InitGame()
    {
    }
}
