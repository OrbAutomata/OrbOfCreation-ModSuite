using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class SnapshotLoadout<T>
{
    protected Stacked.StackedIdRecord<T> record = new Stacked.StackedIdRecord<T>();
    public bool SuppressSave { get; set; }
    public bool SuppressClear { get; set; }
    public bool IsEmpty() => record.GetTotalStacks() == 0;
    public void Clear()
    {
        if (!SuppressClear) record = new Stacked.StackedIdRecord<T>();
    }
    public Stacked.StackedIdRecord<T> GetRecord() =>
        new Stacked.StackedIdRecord<T>(record);
    public void SaveSnapshot(Stacked.StackedIdRecord<T> value)
    {
        if (!SuppressSave) record = new Stacked.StackedIdRecord<T>(value);
    }
}
