using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class OptionViewModel(RuleOption option, GroupViewModel group) : ObservableObject
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _hasError;

    public RuleOption Option { get; } = option;
    public GroupViewModel Group { get; } = group;
    public string Id => Option.Id;
    public string DisplayDescription => Group.UseEnglishDescription
        ? EnglishDisplayDescription()
        : ChineseDisplayDescription();
    public string SummaryText => $"{Id}: {DisplayDescription}";
    public string Description => DisplayDescription;
    public string Detail => Group.UseEnglishDescription
        ? Fallback(Versioned("DescriptionEn"), Versioned("EnglishDescription"), Option.EnglishDescription, Versioned("ShortDescriptionEn"), Versioned("EnglishShortDescription"), Option.EnglishShortDescription, Option.ShortDescription, Option.Description)
        : Fallback(Versioned("Description"), Option.Description);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!value && _isSelected && Group.IsMandatory && !Group.AllowsMultiple && Group.Options.Count(option => option.IsSelected) == 1)
            {
                OnPropertyChanged(nameof(IsSelected));
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CanToggle));
                Group.HandleSelectionChanged(this);
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool CanToggle => IsAvailable || IsSelected;

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
    }

    internal void SetState(bool isAvailable, bool hasError)
    {
        IsAvailable = isAvailable;
        HasError = hasError;
    }

    internal void RefreshDisplay()
    {
        OnPropertyChanged(nameof(DisplayDescription));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(SummaryText));
    }

    private string ChineseDisplayDescription() => Group.UseFullDescription
        ? Fallback(Versioned("Description"), Option.Description, Versioned("ShortDescription"), Option.ShortDescription)
        : Fallback(Versioned("ShortDescription"), Option.ShortDescription, Versioned("Description"), Option.Description);

    private string EnglishDisplayDescription() => Group.UseFullDescription
        ? Fallback(Versioned("DescriptionEn"), Versioned("EnglishDescription"), Option.EnglishDescription, Versioned("ShortDescriptionEn"), Versioned("EnglishShortDescription"), Option.EnglishShortDescription, Option.ShortDescription, Option.Description)
        : Fallback(Versioned("ShortDescriptionEn"), Versioned("EnglishShortDescription"), Option.EnglishShortDescription, Option.ShortDescription, Versioned("DescriptionEn"), Versioned("EnglishDescription"), Option.EnglishDescription, Option.Description);

    private string Versioned(string baseName) => Option.Attribute($"{baseName}{Group.CurrentVersion}");

    private static string Fallback(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
