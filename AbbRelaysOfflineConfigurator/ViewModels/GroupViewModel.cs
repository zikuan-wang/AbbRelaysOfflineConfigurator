using System.Collections.ObjectModel;
using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class GroupViewModel : ObservableObject
{
    private readonly ConfiguratorViewModel _owner;
    private bool _isExpanded = true;
    private int _errorCount;

    public GroupViewModel(OptionGroup group, ConfiguratorViewModel owner)
    {
        Group = group;
        _owner = owner;
        Options = new ObservableCollection<OptionViewModel>(
            group.Options.Select(option => new OptionViewModel(option, this)));
    }

    public OptionGroup Group { get; }
    public string Name => Group.Name;
    public string DisplayName => UseEnglishDescription && !string.IsNullOrWhiteSpace(Group.EnglishName)
        ? Group.EnglishName
        : Group.Name;
    public string SelectionMode => UseEnglishDescription
        ? $"{(Group.IsMandatory ? "Required" : "Optional")} · {(AllowsMultiple ? "Multiple select" : "Single select")}"
        : $"{(Group.IsMandatory ? "必选" : "可选")} · {(AllowsMultiple ? "多选" : "单选")}";
    public bool IsMandatory => Group.IsMandatory;
    public bool IsMultiple => Group.IsMultiple;
    public bool AllowsMultiple => _owner.AllowsMultiple(Group);
    internal bool UseFullDescription => _owner.UseFullDescription;
    internal bool UseEnglishDescription => _owner.IsEnglish;
    public ObservableCollection<OptionViewModel> Options { get; }
    public IEnumerable<RuleOption> SelectedOptions => Options.Where(option => option.IsSelected).Select(option => option.Option);
    public string SelectedSummary
    {
        get
        {
            var selected = Options.Where(option => option.IsSelected).Select(option => option.SummaryText).ToList();
            if (selected.Count == 0)
            {
                return UseEnglishDescription
                    ? IsMandatory ? "Not selected" : "Optional"
                    : IsMandatory ? "未选择" : "可选";
            }

            return string.Join("; ", selected);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            if (SetProperty(ref _errorCount, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorSummary));
            }
        }
    }

    public bool HasError => ErrorCount > 0;
    public string ErrorSummary => _owner.IsEnglish ? $"{ErrorCount} issue(s)" : $"需处理 {ErrorCount}";

    internal void HandleSelectionChanged(OptionViewModel changed)
    {
        if (changed.IsSelected && !AllowsMultiple)
        {
            foreach (var option in Options.Where(option => !ReferenceEquals(option, changed)))
            {
                option.SetSelectedSilently(false);
            }
        }

        _owner.Recalculate();
        OnPropertyChanged(nameof(SelectedSummary));
    }

    internal void RefreshSelectedSummary() => OnPropertyChanged(nameof(SelectedSummary));

    internal void RefreshSelectionMode()
    {
        OnPropertyChanged(nameof(AllowsMultiple));
        OnPropertyChanged(nameof(SelectionMode));
    }

    internal void RefreshValidationState() => ErrorCount = Options.Count(option => option.HasError);

    internal void RefreshDisplayMode()
    {
        foreach (var option in Options)
        {
            option.RefreshDisplay();
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SelectionMode));
        OnPropertyChanged(nameof(ErrorSummary));
        RefreshSelectedSummary();
    }

    public void SelectDefault()
    {
        if (!Options.Any())
        {
            return;
        }

        var defaults = Options.Where(option => option.Option.IsDefault).ToList();
        if (defaults.Count > 0)
        {
            if (AllowsMultiple)
            {
                foreach (var option in defaults)
                {
                    option.SetSelectedSilently(true);
                }
            }
            else
            {
                defaults[0].SetSelectedSilently(true);
            }

            OnPropertyChanged(nameof(SelectedSummary));
            return;
        }

        if (Group.IsMandatory || Group.IsMainCode)
        {
            Options[0].SetSelectedSilently(true);
        }

        OnPropertyChanged(nameof(SelectedSummary));
    }
}
