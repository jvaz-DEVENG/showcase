namespace GameBoost.Core.Abstractions;

public sealed record PowerPlan(Guid Id, string Name);

public interface IPowerService
{
    PowerPlan? GetActivePlan();
    IReadOnlyList<PowerPlan> GetPlans();
    bool SetActivePlan(Guid planId);
}
