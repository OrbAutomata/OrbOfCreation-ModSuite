using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ExperienceContainer : IExperienceElement
{
    private BigDouble experience;
    private int currentLevel;
    private BigDouble cachedRequiredXp = new BigDouble(1, 100);
    public List<BigDouble> Grants { get; } = new List<BigDouble>();
    public Action? AfterGainExperience { get; set; }
    public bool SuppressGain { get; set; }

    public void SetState(int level, BigDouble currentExperience, BigDouble requiredPerLevel)
    {
        currentLevel = level;
        experience = currentExperience;
        cachedRequiredXp = requiredPerLevel;
    }

    public void GainExperience(BigDouble amount)
    {
        Grants.Add(amount);
        if (!SuppressGain) experience = Add(experience, amount);
        AfterGainExperience?.Invoke();
    }

    public int GetGainedLevels()
    {
        var gained = 0;
        while (Compare(experience, cachedRequiredXp) >= 0 && gained < 10000)
        {
            experience = Subtract(experience, cachedRequiredXp);
            currentLevel++;
            gained++;
        }
        return gained;
    }

    public BigDouble GetExperience() => experience;

    public BigDouble GetRequiredExperience() => cachedRequiredXp;

    public int GetLevel() => currentLevel;

    public int GetExperienceLevel() => currentLevel;

    public bool IsForceVisible() => false;

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;

    private static BigDouble Subtract(BigDouble left, BigDouble right)
    {
        var result = left - right;
        return result.Mantissa <= 0 ? default : result;
    }

    private static int Compare(BigDouble left, BigDouble right) => left.CompareTo(right);
    public BigDouble CachedRequiredXp { get => cachedRequiredXp; set => cachedRequiredXp = value; }
}
