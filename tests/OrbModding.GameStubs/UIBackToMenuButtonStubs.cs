using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class UIBackToMenuButton : UnityEngine.MonoBehaviour
{
    private readonly UnityEngine.UI.Button button;
    public VoidEventChannel manualSave = new();

    public UIBackToMenuButton()
    {
        var root = new UnityEngine.GameObject("Back to Menu");
        gameObject = root;
        transform = root.transform;
        name = root.name;
        button = root.AddComponent<UnityEngine.UI.Button>();
    }

    public void SetLiveForTest(bool value)
    {
        gameObject.SetActive(value);
        button.interactable = value;
    }

    /// <summary>The instantiated copy the game parks inside a modal's content area.</summary>
    public void PlaceInPanelForTest(UIModal panel)
    {
        transform.SetParent(panel.transform, false);
        gameObject.SetActive(false);
    }

    public void BackToMenu()
    {
        if (manualSave is not null) manualSave.Raise();
        SaveStateManager.instance.BackToMainMenu();
    }
}
