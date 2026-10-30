using System;
using UnityEngine.SceneManagement;

namespace OrbMentor;

internal static class MentorGameplayScene
{
    internal static bool IsActive() => Is(SceneManager.GetActiveScene().name);

    internal static bool Is(string sceneName) =>
        string.Equals(sceneName, "Main", StringComparison.Ordinal);
}
