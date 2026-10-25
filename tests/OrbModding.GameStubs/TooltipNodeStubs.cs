using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class TooltipNode
{
    public enum NodeType { Text, Icon, IconText, Divider, Parent }
    public enum ParentType { Plain, Boxed, SoftBoxed }

    public TooltipNode(string text)
    {
        this.text = text;
        textColor = UnityEngine.Color.white;
        color = UnityEngine.Color.white;
    }

    public TooltipNode(string text, UnityEngine.Color color)
        : this(text)
    {
        this.color = color;
    }

    public string Text => text;
    public UnityEngine.Color? Color => color;
    public List<TooltipNode> children = new();
    public UnityEngine.Color color;
    public UnityEngine.Sprite? icon;
    public bool isIconBacked;
    public NodeType nodeType;
    public ParentType parentType;
    public float size = 1f;
    public List<ITooltipable> subTooltips = new();
    public string text;
    public UnityEngine.Color textColor;
    public Func<string>? textFn;
    public ITooltipable? tooltipable;
}
