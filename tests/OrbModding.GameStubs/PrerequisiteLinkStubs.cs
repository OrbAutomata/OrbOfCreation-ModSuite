using System.Collections.Generic;

public sealed class PrerequisiteLinkSO : IdScriptableObject
{
    public sealed class LinkDefinition
    {
        public string elementName = string.Empty;
        public Prerequisites.Container prerequisites = new();
        public bool isActiveEnabled = true;
        public bool isPassiveEnabled;
        public long currentFrame = -1;
    }

    public static List<PrerequisiteLinkSO> All = new();

    public List<LinkDefinition> linkTiers = new();
}
