using System.Collections;

namespace FindJobHelper.Configuration;

public sealed class SelectionConfiguration
{
    public SelectionOptionsConfiguration Default { get; init; } = new();
    public SelectionOptionsConfiguration Education { get; init; } = new();
    public SelectionOptionsConfiguration WorkExperience { get; init; } = new();
    public SelectionOptionsConfiguration PersonalProjects { get; init; } = new();

    public void CollectValidationErrors(List<string> errors)
    {
        if (Default is null)
        {
            errors.Add("'selection.default' must be an object when supplied.");
        }

        Default?.CollectValidationErrors("selection.default", errors);

        if (Education is null)
        {
            errors.Add("'selection.education' must be an object when supplied.");
        }

        if (WorkExperience is null)
        {
            errors.Add("'selection.workExperience' must be an object when supplied.");
        }

        if (PersonalProjects is null)
        {
            errors.Add("'selection.personalProjects' must be an object when supplied.");
        }

        Education?.CollectValidationErrors("selection.education", errors);
        WorkExperience?.CollectValidationErrors("selection.workExperience", errors);
        PersonalProjects?.CollectValidationErrors("selection.personalProjects", errors);
    }
}

public static class SelectionConfigurationExtensions
{
    extension(SelectionConfiguration configuration)
    {
        public SelectionOptionsEnumerable Options => new(configuration);
    }
}

public readonly struct SelectionOptionsEnumerable(SelectionConfiguration configuration)
    : IEnumerable<SelectionOptionsConfiguration>
{
    public IEnumerator<SelectionOptionsConfiguration> GetEnumerator()
    {
        yield return configuration.Default;
        yield return configuration.Education;
        yield return configuration.WorkExperience;
        yield return configuration.PersonalProjects;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public struct SelectionOptionsFieldMask
{
    public bool MinItemBudget { get; set; }
    public bool ItemBudget { get; set; }
    public bool ScoreLowerBound { get; set; }
    public bool RecencyBoost { get; set; }
    public bool DirectMatchBoost { get; set; }
}

public sealed class SelectionOptionsConfiguration
{
    private int _minItemBudget;
    private int? _itemBudget;
    private float _scoreLowerBound;
    private float _recencyBoost;
    private float? _directMatchBoost = 0;

    public SelectionOptionsFieldMask SpecifiedFields;

    public int MinItemBudget
    {
        get => _minItemBudget;
        init
        {
            _minItemBudget = value;
            SpecifiedFields.MinItemBudget = true;
        }
    }

    public int? ItemBudget
    {
        get => _itemBudget;
        init
        {
            _itemBudget = value;
            SpecifiedFields.ItemBudget = true;
        }
    }

    public float ScoreLowerBound
    {
        get => _scoreLowerBound;
        init
        {
            _scoreLowerBound = value;
            SpecifiedFields.ScoreLowerBound = true;
        }
    }

    public float RecencyBoost
    {
        get => _recencyBoost;
        init
        {
            _recencyBoost = value;
            SpecifiedFields.RecencyBoost = true;
        }
    }

    public float? DirectMatchBoost
    {
        get => _directMatchBoost;
        init
        {
            _directMatchBoost = value;
            SpecifiedFields.DirectMatchBoost = true;
        }
    }

    public void CollectValidationErrors(string path, List<string> errors)
    {
        if (MinItemBudget < 0)
        {
            errors.Add($"'{path}.minItemBudget' must be non-negative.");
        }

        if (ItemBudget is < 0)
        {
            errors.Add($"'{path}.itemBudget' must be non-negative.");
        }
        else if (MinimumExceedsTotalBudget())
        {
            errors.Add(
                $"'{path}.minItemBudget' must not exceed the total item budget.");
        }

        if (!float.IsFinite(ScoreLowerBound) || ScoreLowerBound < 0)
        {
            errors.Add($"'{path}.scoreLowerBound' must be finite and non-negative.");
        }

        AddBoostValidationError("recencyBoost", RecencyBoost);
        if (DirectMatchBoost is { } directMatchBoost)
        {
            AddBoostValidationError("directMatchBoost", directMatchBoost);
        }

        void AddBoostValidationError(string jsonPropertyName, float value)
        {
            if (IsValidBoost(value))
            {
                return;
            }

            errors.Add(
                $"'{path}.{jsonPropertyName}' must be finite and non-negative.");
        }

        bool MinimumExceedsTotalBudget()
        {
            if (ItemBudget is not { } itemBudget)
            {
                return false;
            }

            return MinItemBudget > itemBudget;
        }
    }

    // Mirrors the engine boost invariant without referencing Core:
    // keep in sync with FindJobHelper.Core.ScoreBoost.IsValid.
    private static bool IsValidBoost(float value)
    {
        return float.IsFinite(value) && value >= 0;
    }
}
