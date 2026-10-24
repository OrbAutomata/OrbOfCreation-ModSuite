using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class Duration
{
    public sealed class Entry
    {
        public double duration;
        public double mult;
        public ProcessorType type;
    }

    public enum ProcessorType { Fixed }
}
