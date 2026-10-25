/// <summary>Fixture string IDs use the identity primitive shared with tooltipable entities.</summary>
public class StringIdScriptableObject : IdScriptableObject
{
    public new string uuid { get => StringUuid; set => StringUuid = value; }
}
