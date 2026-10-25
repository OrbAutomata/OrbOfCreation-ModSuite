using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class UnityObjectLifetimeTests
{
    [Fact]
    public void DeferredGameObjectDestructionRemovesOwnedSubtreeAndPreservesSibling()
    {
        var parent = new GameObject("Parent");
        var doomed = new GameObject("Doomed");
        doomed.transform.SetParent(parent.transform, false);
        var child = new GameObject("Child");
        child.transform.SetParent(doomed.transform, false);
        var label = child.AddComponent<TMPro.TextMeshProUGUI>();
        var sibling = new GameObject("Sibling");
        sibling.transform.SetParent(parent.transform, false);

        UnityEngine.Object.Destroy(doomed);
        Assert.Equal(2, parent.transform.childCount);
        Assert.False(doomed == null);
        UnityEngine.Object.FlushDestroyQueue();

        Assert.True(doomed == null);
        Assert.True(child == null);
        Assert.True(label == null);
        Assert.Single(parent.GetComponents<Transform>());
        Assert.Equal(1, parent.transform.childCount);
        Assert.Same(sibling.transform, parent.transform.GetChild(0));
        Assert.Null(doomed!.transform.parent);
        Assert.False(doomed.activeSelf);
        UnityEngine.Object.Destroy(parent);
        UnityEngine.Object.FlushDestroyQueue();
    }

    [Fact]
    public void DeferredComponentDestructionKeepsItsOwnerAndOtherComponents()
    {
        var owner = new GameObject("Owner");
        var removed = owner.AddComponent<UnityEngine.UI.Image>();
        var kept = owner.AddComponent<UnityEngine.UI.Button>();

        UnityEngine.Object.Destroy(removed);
        Assert.Same(removed, owner.GetComponent<UnityEngine.UI.Image>());
        UnityEngine.Object.FlushDestroyQueue();

        Assert.True(removed == null);
        Assert.Null(owner.GetComponent<UnityEngine.UI.Image>());
        Assert.Same(kept, owner.GetComponent<UnityEngine.UI.Button>());
        Assert.False(owner == null);
        UnityEngine.Object.Destroy(owner);
        UnityEngine.Object.FlushDestroyQueue();
    }
}
