using System;

public class ObservingCache
{
    public class CalculatedValue<T>
    {
        private readonly Func<T> calcFn;
        private T calcValue;

        public CalculatedValue(Func<T> calcFn)
        {
            this.calcFn = calcFn;
            calcValue = calcFn();
        }

        public T Calculate()
        {
            calcValue = calcFn();
            return calcValue;
        }
    }
}

namespace Engagement
{
    public class EngagedEffectInstance
    {
        public bool isCompleteOnce;
        private BigDouble duration;
        private BigDouble maxDuration;
        private ObservingCache.CalculatedValue<BigDouble> getMaxDuration;

        public EngagedEffectInstance(BigDouble duration, BigDouble maxDuration,
            ObservingCache.CalculatedValue<BigDouble> getMaxDuration)
        {
            this.duration = duration;
            this.maxDuration = maxDuration;
            this.getMaxDuration = getMaxDuration;
        }

        public BigDouble GetCurrentDuration() => duration;

        public BigDouble GetMaxDuration() => maxDuration;
    }
}
