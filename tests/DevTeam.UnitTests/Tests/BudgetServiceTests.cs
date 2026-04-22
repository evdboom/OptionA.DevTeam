namespace DevTeam.UnitTests.Tests;

internal static class BudgetServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SelectModel_ReturnsDefault_WhenNoPool", SelectModel_ReturnsDefault_WhenNoPool),
        new("SelectModel_ReturnsFromPool_WhenAffordable", SelectModel_ReturnsFromPool_WhenAffordable),
        new("SelectModel_ReturnsFallback_WhenPrimaryUnaffordable", SelectModel_ReturnsFallback_WhenPrimaryUnaffordable),
        new("CanAffordModel_ReturnsFalse_WhenOverBudget", CanAffordModel_ReturnsFalse_WhenOverBudget),
        new("CanAffordModel_ReturnsFalse_WhenBudgetExhausted", CanAffordModel_ReturnsFalse_WhenBudgetExhausted),
        new("CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget", CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget),
        new("CommitCredits_IncreasesBudgetUsed", CommitCredits_IncreasesBudgetUsed),
        new("CommitCredits_AccumulatesAcrossMultipleCalls", CommitCredits_AccumulatesAcrossMultipleCalls),
        new("InferFamily_DetectsOpenAI", InferFamily_DetectsOpenAI),
        new("InferFamily_DetectsAnthropic", InferFamily_DetectsAnthropic),
        new("InferFamily_DetectsGoogle", InferFamily_DetectsGoogle),
        new("InferFamily_FallsBackToOther", InferFamily_FallsBackToOther),
        new("SelectModel_PrefersOtherFamily_WhenExcludeFamilySet", SelectModel_PrefersOtherFamily_WhenExcludeFamilySet),
        new("SelectModel_FallsBackToSameFamily_WhenNoCrossOption", SelectModel_FallsBackToSameFamily_WhenNoCrossOption),
        new("EstimateCostUsd_UsesConfiguredTokenRates", EstimateCostUsd_UsesConfiguredTokenRates),
        new("SelectModel_PremiumModel_RejectedFromPool_WhenLowRatioAndExceedsPerRunPremiumLimit", SelectModel_PremiumModel_RejectedFromPool_WhenLowRatioAndExceedsPerRunPremiumLimit),
        new("SelectModel_PremiumModel_AcceptedFromPool_WhenWithinPerRunPremiumLimit", SelectModel_PremiumModel_AcceptedFromPool_WhenWithinPerRunPremiumLimit),
        new("SelectModel_StandardModel_RejectedByRatio_WhenCostExceedsPerRunCreditLimit", SelectModel_StandardModel_RejectedByRatio_WhenCostExceedsPerRunCreditLimit),
        new("SelectModel_StandardModel_AcceptedByPerRunCreditLimit_DespiteLowBudgetRatio", SelectModel_StandardModel_AcceptedByPerRunCreditLimit_DespiteLowBudgetRatio),
    ];

    private const string Gpt54 = "gpt-5.4";
    private const string ClaudeSonnet46 = "claude-sonnet-4.6";
    private const string ClaudeHaiku45 = "claude-haiku-4.5";
    private const string ClaudeOpus46 = "claude-opus-4.6";
    private const string Anthropic = "anthropic";
    private const string Reviewer = "reviewer";

    // "user" role policy has no model pool, primary="gpt-5-mini", fallback="gpt-5-mini"
    // With no models in state.Models, defaultModel is built as { Name="gpt-5-mini", Cost=0 }
    private static Task SelectModel_ReturnsDefault_WhenNoPool()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        // No models → defaultModel fallback = { Name="gpt-5-mini", Cost=0 }

        var model = svc.SelectModelForRole(state, "user");

        Assert.That(model.Name == "gpt-5-mini", $"Expected gpt-5-mini but got '{model.Name}'");
        Assert.That(Math.Abs(model.Cost) < double.Epsilon, $"Expected cost 0 but got {model.Cost}");
        return Task.CompletedTask;
    }

    // "developer" policy has pool = ["gpt-5.4", "claude-sonnet-4.6"]
    private static Task SelectModel_ReturnsFromPool_WhenAffordable()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = Gpt54, Cost = 1 },
                new ModelDefinition { Name = ClaudeSonnet46, Cost = 1 }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0 }
        };

        var model = svc.SelectModelForRole(state, "developer");

        var poolNames = new[] { Gpt54, ClaudeSonnet46 };
        Assert.That(poolNames.Contains(model.Name), $"Expected pool model but got '{model.Name}'");
        return Task.CompletedTask;
    }

    // "planner" policy has no pool. Primary = "claude-sonnet-4.6" (Cost=1).
    // Set cap so primary is unaffordable; fallback "claude-haiku-4.5" (Cost=0.1) is affordable.
    private static Task SelectModel_ReturnsFallback_WhenPrimaryUnaffordable()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeSonnet46, Cost = 1 },
                new ModelDefinition { Name = ClaudeHaiku45, Cost = 0.1 }
            ],
            Budget = new BudgetState { TotalCreditCap = 0.5, CreditsCommitted = 0 }
        };

        var model = svc.SelectModelForRole(state, "planner");

        Assert.That(model.Name == ClaudeHaiku45, $"Expected {ClaudeHaiku45} but got '{model.Name}'");
        return Task.CompletedTask;
    }

    private static Task CanAffordModel_ReturnsFalse_WhenOverBudget()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 5, CreditsCommitted = 5 }
        };
        var model = new ModelDefinition { Name = "any-model", Cost = 1 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(!result, "Expected CanAffordModel to return false when over budget");
        return Task.CompletedTask;
    }

    private static Task CommitCredits_IncreasesBudgetUsed()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        var model = new ModelDefinition { Name = Gpt54, Cost = 2 };

        svc.CommitCredits(state, model);

        Assert.That(Math.Abs(state.Budget.CreditsCommitted - 2) < double.Epsilon,
            $"Expected CreditsCommitted=2 but got {state.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }

    private static Task CanAffordModel_ReturnsFalse_WhenBudgetExhausted()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 5, CreditsCommitted = 5 }
        };
        // Budget exactly at cap; any positive-cost model must be rejected
        var model = new ModelDefinition { Name = "any-model", Cost = 1 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(!result, "Expected CanAffordModel to return false when budget is exactly at cap and model has positive cost");
        return Task.CompletedTask;
    }

    private static Task CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 10, CreditsCommitted = 8 }
        };
        // remaining = 2, cost = 2 → totalAfter = 10 == cap → should be affordable (not strictly over)
        var model = new ModelDefinition { Name = "exact-model", Cost = 2 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(result, "Expected CanAffordModel to return true when committed + cost == cap exactly");
        return Task.CompletedTask;
    }

    private static Task CommitCredits_AccumulatesAcrossMultipleCalls()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        var model = new ModelDefinition { Name = Gpt54, Cost = 1 };

        svc.CommitCredits(state, model);
        svc.CommitCredits(state, model);
        svc.CommitCredits(state, model);

        Assert.That(Math.Abs(state.Budget.CreditsCommitted - 3) < double.Epsilon,
            $"Expected CreditsCommitted=3 after 3 commits but got {state.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsOpenAI()
    {
        Assert.That(ModelDefinition.InferFamily(Gpt54) == "openai", $"{Gpt54} should be openai");
        Assert.That(ModelDefinition.InferFamily("o1-preview") == "openai", "o1-preview should be openai");
        Assert.That(ModelDefinition.InferFamily("o3-mini") == "openai", "o3-mini should be openai");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsAnthropic()
    {
        Assert.That(ModelDefinition.InferFamily(ClaudeSonnet46) == Anthropic, $"{ClaudeSonnet46} should be {Anthropic}");
        Assert.That(ModelDefinition.InferFamily("claude-opus-4.5") == Anthropic, $"claude-opus-4.5 should be {Anthropic}");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsGoogle()
    {
        Assert.That(ModelDefinition.InferFamily("gemini-3.1-pro-preview") == "google", "gemini-3.1-pro-preview should be google");
        return Task.CompletedTask;
    }

    private static Task InferFamily_FallsBackToOther()
    {
        Assert.That(ModelDefinition.InferFamily("llama-3-70b") == "other", "llama-3-70b should be other");
        Assert.That(ModelDefinition.InferFamily("") == "other", "empty should be other");
        return Task.CompletedTask;
    }

    // reviewer policy: primary = claude-opus-4.6 (anthropic). When excludeFamily=anthropic,
    // gpt-5.4 (openai) should be selected from the explicit model list.
    private static Task SelectModel_PrefersOtherFamily_WhenExcludeFamilySet()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeOpus46, Cost = 1, IsPremium = true },
                new ModelDefinition { Name = Gpt54, Cost = 1, IsPremium = false }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0, PremiumCreditCap = 100 }
        };

        var model = svc.SelectModelForRole(state, Reviewer, excludeFamily: Anthropic);

        Assert.That(model.Name == Gpt54, $"Expected {Gpt54} (non-{Anthropic}) but got '{model.Name}'");
        return Task.CompletedTask;
    }

    // When all models are the same family as excludeFamily, fall back gracefully.
    private static Task SelectModel_FallsBackToSameFamily_WhenNoCrossOption()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeOpus46, Cost = 1, IsPremium = true },
                new ModelDefinition { Name = ClaudeHaiku45, Cost = 0.1, IsPremium = false }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0, PremiumCreditCap = 100 }
        };

        // All models are anthropic; excludeFamily = anthropic → should still return a model (not null)
        var model = svc.SelectModelForRole(state, Reviewer, excludeFamily: Anthropic);

        Assert.That(model is not null, "Expected a model even when no cross-family option exists");
        Assert.That(!string.IsNullOrEmpty(model!.Name), "Expected a non-empty model name when falling back");
        return Task.CompletedTask;
    }

    // reviewer policy: AllowPremium=true, pool=[claude-opus-4.6, claude-opus-4.7], fallback=gpt-5.4.
    // budgetRatio=0.25 (25% remaining) is below the 30% premium threshold.
    // Cost=3 > PerRunPremiumLimit=2 → per-run override does NOT apply → rejected by ratio.
    // Non-premium fallback gpt-5.4 (Cost=1 <= PerRunCreditLimit=5) is selected instead.
    private static Task SelectModel_PremiumModel_RejectedFromPool_WhenLowRatioAndExceedsPerRunPremiumLimit()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeOpus46, Cost = 3, IsPremium = true },
                new ModelDefinition { Name = Gpt54, Cost = 1, IsPremium = false }
            ],
            Budget = new BudgetState
            {
                TotalCreditCap = 100,
                CreditsCommitted = 75,   // budgetRatio = 0.25, below 0.30 premium threshold
                PremiumCreditCap = 100,
                PerRunPremiumLimit = 2   // Cost=3 exceeds this, so per-run bypass does not apply
            }
        };

        var model = svc.SelectModelForRole(state, Reviewer);

        Assert.That(model.Name == Gpt54,
            $"Expected non-premium fallback {Gpt54} but got '{model.Name}' (premium should be rejected by budget ratio)");
        return Task.CompletedTask;
    }

    // Same low budgetRatio=0.25, but Cost=1.5 <= PerRunPremiumLimit=2 → per-run override applies → accepted.
    private static Task SelectModel_PremiumModel_AcceptedFromPool_WhenWithinPerRunPremiumLimit()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeOpus46, Cost = 1.5, IsPremium = true },
                new ModelDefinition { Name = Gpt54, Cost = 1, IsPremium = false }
            ],
            Budget = new BudgetState
            {
                TotalCreditCap = 100,
                CreditsCommitted = 75,   // budgetRatio = 0.25, below 0.30 premium threshold
                PremiumCreditCap = 100,
                PerRunPremiumLimit = 2   // Cost=1.5 <= 2 → per-run bypass fires → accepted
            }
        };

        var model = svc.SelectModelForRole(state, Reviewer);

        Assert.That(model.Name == ClaudeOpus46,
            $"Expected premium {ClaudeOpus46} (within per-run premium limit) but got '{model.Name}'");
        return Task.CompletedTask;
    }

    // planner policy: no pool, primary=claude-sonnet-4.6, fallback=claude-haiku-4.5, AllowPremium=false.
    // budgetRatio=0.10 (10% remaining) is below the 15% standard (Cost>=1) threshold.
    // Cost=2 > PerRunCreditLimit=1 → per-run bypass does NOT apply → rejected by ratio.
    // Fallback claude-haiku-4.5 (Cost=0.1 <= PerRunCreditLimit=1) is selected instead.
    private static Task SelectModel_StandardModel_RejectedByRatio_WhenCostExceedsPerRunCreditLimit()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeSonnet46, Cost = 2, IsPremium = false },
                new ModelDefinition { Name = ClaudeHaiku45, Cost = 0.1, IsPremium = false }
            ],
            Budget = new BudgetState
            {
                TotalCreditCap = 100,
                CreditsCommitted = 90,   // budgetRatio = 0.10, below 0.15 threshold for Cost>=1
                PremiumCreditCap = 100,
                PerRunCreditLimit = 1    // Cost=2 exceeds this, so per-run bypass does not apply
            }
        };

        var model = svc.SelectModelForRole(state, "planner");

        Assert.That(model.Name == ClaudeHaiku45,
            $"Expected cheap fallback {ClaudeHaiku45} but got '{model.Name}' (primary should be rejected by budget ratio)");
        return Task.CompletedTask;
    }

    // Same low budgetRatio=0.10, but PerRunCreditLimit=5 so Cost=2 <= 5 → per-run bypass applies → primary accepted.
    private static Task SelectModel_StandardModel_AcceptedByPerRunCreditLimit_DespiteLowBudgetRatio()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = ClaudeSonnet46, Cost = 2, IsPremium = false },
                new ModelDefinition { Name = ClaudeHaiku45, Cost = 0.1, IsPremium = false }
            ],
            Budget = new BudgetState
            {
                TotalCreditCap = 100,
                CreditsCommitted = 90,   // budgetRatio = 0.10, below 0.15 threshold for Cost>=1
                PremiumCreditCap = 100,
                PerRunCreditLimit = 5    // Cost=2 <= 5 → per-run bypass fires → accepted despite low ratio
            }
        };

        var model = svc.SelectModelForRole(state, "planner");

        Assert.That(model.Name == ClaudeSonnet46,
            $"Expected primary {ClaudeSonnet46} (within per-run credit limit) but got '{model.Name}'");
        return Task.CompletedTask;
    }

    private static Task EstimateCostUsd_UsesConfiguredTokenRates()
    {
        var model = new ModelDefinition
        {
            Name = Gpt54,
            InputCostPer1kTokens = 0.01,
            OutputCostPer1kTokens = 0.03
        };

        var estimated = model.EstimateCostUsd(inputTokens: 1500, outputTokens: 500);

        Assert.That(estimated.HasValue, "Expected EstimateCostUsd to return a value");
        Assert.That(Math.Abs(estimated!.Value - 0.03) < double.Epsilon, $"Expected USD estimate 0.03 but got {estimated}");
        return Task.CompletedTask;
    }
}
