using System;
namespace EveEngine
{
    public sealed class EveEntityPreparationBudget
    {
        public int EntitiesPerFrame { get; }
        public int StepsPerFrame { get; }
        public double MillisecondsPerFrame { get; }
        public EveEntityPreparationBudget(int entitiesPerFrame, int stepsPerFrame, double millisecondsPerFrame)
        {
            if (entitiesPerFrame < 1 || stepsPerFrame < 1 || double.IsNaN(millisecondsPerFrame) || double.IsInfinity(millisecondsPerFrame) || millisecondsPerFrame <= 0) throw new ArgumentOutOfRangeException();
            EntitiesPerFrame = entitiesPerFrame; StepsPerFrame = stepsPerFrame; MillisecondsPerFrame = millisecondsPerFrame;
        }
    }
}
