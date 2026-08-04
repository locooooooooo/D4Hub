using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using D4Hub.Core;

namespace D4Hub.App.ViewModels;

public sealed record LootFilterClassOption(string Label, string? ClassName);
public sealed record LootFilterStageOption(string Label, LootFilterStage? Stage);
public sealed record LootFilterUseCaseOption(string Label, string? UseCase);
public sealed record LootFilterSortOption(string Label, string Value);

public sealed class LootFilterCollectionViewModel : ObservableObject
{
    private readonly FileLootFilterStore _localStore;
    private readonly HashSet<string> _bundledIds;
    private LootFilterClassOption _selectedClass = new("全部职业", null);
    private LootFilterStageOption _selectedStage = new("全部阶段", null);
    private LootFilterUseCaseOption _selectedUseCase = new("全部用途", null);
    private LootFilterSortOption _selectedSort = new("推荐优先", "recommended");
    private LootFilterPreset? _selectedFilter;
    private string _searchText = string.Empty;
    private string _importName = "我的过滤器";
    private string _importClassName = "Druid";
    private LootFilterStageOption _importStage;
    private string _importBuildId = string.Empty;
    private int _importVariantIndex;
    private string _importSourceUrl = string.Empty;
    private string _importDescription = string.Empty;
    private string _importCode = string.Empty;
    private string _status = "选择一个阶段过滤器，或粘贴网站复制码建立本地记录。";

    public LootFilterCollectionViewModel(
        IReadOnlyList<LootFilterPreset> bundledFilters,
        FileLootFilterStore localStore)
    {
        _localStore = localStore;
        _bundledIds = bundledFilters.Select(filter => filter.Id).ToHashSet(StringComparer.Ordinal);
        _importStage = StageFilters[1];

        var localFilters = localStore.LoadAll();
        foreach (var filter in bundledFilters.Concat(localFilters).GroupBy(GetIdentity, StringComparer.Ordinal))
        {
            Filters.Add(filter.Last());
        }

        ImportCommand = new RelayCommand(ImportFilter);
        CopySelectedCommand = new RelayCommand(CopySelectedFilter, () => SelectedFilter is not null);
        ClearImportCommand = new RelayCommand(ClearImport);
        RefreshFilterOptions();
        SelectedFilter = FilteredFilters.FirstOrDefault();
    }

    public ObservableCollection<LootFilterPreset> Filters { get; } = new();

    public IReadOnlyList<LootFilterStageOption> StageFilters { get; } =
    [
        new("全部阶段", null),
        new("1-70 开荒", LootFilterStage.Leveling),
        new("前期", LootFilterStage.Early),
        new("中期", LootFilterStage.Mid),
        new("后期 / 终局", LootFilterStage.Late),
        new("冲层", LootFilterStage.Push),
        new("综合", LootFilterStage.General)
    ];

    public IReadOnlyList<LootFilterStageOption> ImportStages => StageFilters.Skip(1).ToList();

    public ObservableCollection<LootFilterClassOption> ClassFilters { get; } = new();

    public ObservableCollection<LootFilterUseCaseOption> UseCaseFilters { get; } = new();

    public IReadOnlyList<LootFilterSortOption> SortFilters { get; } =
    [
        new("推荐优先", "recommended"),
        new("最近更新", "updated"),
        new("阶段顺序", "stage"),
        new("名称排序", "name")
    ];

    public IEnumerable<LootFilterPreset> FilteredFilters
    {
        get
        {
            var query = Filters.Where(IsMatch);
            return _selectedSort.Value switch
            {
                "updated" => query.OrderByDescending(GetUpdatedAt).ThenBy(filter => filter.Name, StringComparer.Ordinal),
                "stage" => query.OrderBy(filter => GetStageOrder(filter.Stage)).ThenByDescending(filter => filter.IsRecommended).ThenBy(filter => filter.Name, StringComparer.Ordinal),
                "name" => query.OrderBy(filter => filter.Name, StringComparer.Ordinal),
                _ => query.OrderByDescending(filter => filter.IsRecommended)
                    .ThenByDescending(filter => filter.Source.Equals("d2core", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(GetUpdatedAt)
                    .ThenBy(filter => GetStageOrder(filter.Stage))
                    .ThenBy(filter => filter.Name, StringComparer.Ordinal)
            };
        }
    }

    public LootFilterClassOption SelectedClass
    {
        get => _selectedClass;
        set
        {
            if (value is not null && SetProperty(ref _selectedClass, value))
            {
                ApplyFilters();
            }
        }
    }

    public LootFilterStageOption SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (value is not null && SetProperty(ref _selectedStage, value))
            {
                ApplyFilters();
            }
        }
    }

    public LootFilterUseCaseOption SelectedUseCase
    {
        get => _selectedUseCase;
        set
        {
            if (value is not null && SetProperty(ref _selectedUseCase, value))
            {
                ApplyFilters();
            }
        }
    }

    public LootFilterSortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value is not null && SetProperty(ref _selectedSort, value))
            {
                ApplyFilters();
            }
        }
    }

    public LootFilterPreset? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (!SetProperty(ref _selectedFilter, value))
            {
                return;
            }

            if (value is not null)
            {
                ImportName = value.Name;
                ImportClassName = value.ClassName;
                ImportStage = ImportStages.FirstOrDefault(option => option.Stage == value.Stage) ?? ImportStages[0];
                ImportBuildId = value.BuildId;
                ImportVariantIndex = value.VariantIndex;
                ImportSourceUrl = value.SourceUrl;
                ImportDescription = value.Description;
                ImportCode = value.CopyCode;
            }

            OnPropertyChanged(nameof(SelectedFilterCode));
            OnPropertyChanged(nameof(SelectedFilterLegend));
            OnPropertyChanged(nameof(SelectedFilterSourceLabel));
            OnPropertyChanged(nameof(SelectedFilterBuildLabel));
            OnPropertyChanged(nameof(SelectedFilterUseCaseLabel));
            OnPropertyChanged(nameof(SelectedFilterScopeLabel));
            OnPropertyChanged(nameof(SelectedFilterRecommendationLabel));
            OnPropertyChanged(nameof(SelectedFilterFreshnessLabel));
            CopySelectedCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedFilterCode => SelectedFilter?.CopyCode ?? string.Empty;
    public IReadOnlyList<LootFilterLegend> SelectedFilterLegend =>
        SelectedFilter is null ? Array.Empty<LootFilterLegend>() : SelectedFilter.Legend;
    public string SelectedFilterSourceLabel => SelectedFilter is null
        ? "未选择过滤器"
        : $"{SelectedFilter.Source} · {SelectedFilter.BuildId} · {SelectedFilter.VariantLabel}";
    public string SelectedFilterBuildLabel => SelectedFilter is null
        ? "未选择 BD"
        : $"BD：{SelectedFilter.BuildDisplayName}";
    public string SelectedFilterUseCaseLabel => SelectedFilter?.UseCaseLabel ?? "未标注用途";
    public string SelectedFilterScopeLabel => SelectedFilter?.ScopeLabel ?? "未标注范围";
    public string SelectedFilterRecommendationLabel => SelectedFilter?.RecommendationLabel ?? "可用";
    public string SelectedFilterFreshnessLabel => SelectedFilter is null
        ? "来源更新时间未知"
        : SelectedFilter.SourceUpdatedAtLabel;
    public bool HasFilteredFilters => FilteredFilters.Any();
    public string EmptyFilterText => Filters.Count == 0
        ? "还没有过滤器"
        : "没有匹配结果 · 试试清除搜索或放宽筛选";
    public string FilterCountText => $"{Filters.Count} 个过滤器 · 当前显示 {FilteredFilters.Count()} 个";

    public string ImportName
    {
        get => _importName;
        set => SetProperty(ref _importName, value ?? string.Empty);
    }

    public string ImportClassName
    {
        get => _importClassName;
        set => SetProperty(ref _importClassName, value ?? string.Empty);
    }

    public LootFilterStageOption ImportStage
    {
        get => _importStage;
        set => SetProperty(ref _importStage, value ?? ImportStages[0]);
    }

    public string ImportBuildId
    {
        get => _importBuildId;
        set => SetProperty(ref _importBuildId, value ?? string.Empty);
    }

    public int ImportVariantIndex
    {
        get => _importVariantIndex;
        set => SetProperty(ref _importVariantIndex, Math.Max(0, value));
    }

    public string ImportSourceUrl
    {
        get => _importSourceUrl;
        set => SetProperty(ref _importSourceUrl, value ?? string.Empty);
    }

    public string ImportDescription
    {
        get => _importDescription;
        set => SetProperty(ref _importDescription, value ?? string.Empty);
    }

    public string ImportCode
    {
        get => _importCode;
        set => SetProperty(ref _importCode, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RelayCommand ImportCommand { get; }
    public RelayCommand CopySelectedCommand { get; }
    public RelayCommand ClearImportCommand { get; }
    public Action<string>? CopyRequested { get; set; }

    public void SetStatus(string message) => Status = message;

    private void ImportFilter()
    {
        try
        {
            var name = ImportName.Trim();
            var className = ImportClassName.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(className))
            {
                throw new InvalidDataException("请填写过滤器名称和职业。");
            }

            var normalizedCode = LootFilterMetadata.NormalizeCode(ImportCode);
            var source = ImportSourceUrl.Contains("d2core.com", StringComparison.OrdinalIgnoreCase)
                ? "d2core"
                : "manual";
            var imported = new LootFilterPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Source = source,
                BuildId = ImportBuildId.Trim(),
                VariantIndex = ImportVariantIndex,
                Name = name,
                BuildName = name,
                ClassName = className,
                Stage = ImportStage.Stage ?? LootFilterStage.General,
                LevelRange = ImportStage.Stage == LootFilterStage.Leveling ? "1-70" : string.Empty,
                UseCases = [ImportStage.Label],
                SourceUrl = ImportSourceUrl.Trim(),
                Description = ImportDescription.Trim(),
                CopyCode = normalizedCode,
                Legend = CreateDefaultLegend(),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var existing = Filters.FirstOrDefault(filter =>
                string.Equals(GetIdentity(filter), GetIdentity(imported), StringComparison.Ordinal));
            if (existing is not null && _bundledIds.Contains(existing.Id))
            {
                imported.Id = Guid.NewGuid().ToString("N");
                existing = null;
            }

            if (existing is not null)
            {
                imported.Id = existing.Id;
                Filters[Filters.IndexOf(existing)] = imported;
            }
            else
            {
                Filters.Add(imported);
            }

            SaveLocalFilters();
            RefreshFilterOptions();
            SelectedFilter = imported;
            Status = $"已保存 {imported.Name} · {imported.StageLabel}";
        }
        catch (InvalidDataException exception)
        {
            Status = $"导入失败 · {exception.Message}";
        }
    }

    private void CopySelectedFilter()
    {
        if (SelectedFilter is null)
        {
            return;
        }

        CopyRequested?.Invoke(SelectedFilter.CopyCode);
        Status = $"已复制 {SelectedFilter.Name} 的过滤码";
    }

    private void ClearImport()
    {
        ImportName = "我的过滤器";
        ImportClassName = "Druid";
        ImportStage = ImportStages[0];
        ImportBuildId = string.Empty;
        ImportVariantIndex = 0;
        ImportSourceUrl = string.Empty;
        ImportDescription = string.Empty;
        ImportCode = string.Empty;
        Status = "已清空导入表单";
    }

    private void RefreshFilterOptions()
    {
        var selectedClassName = _selectedClass.ClassName;
        var selectedUseCaseValue = _selectedUseCase.UseCase;
        ClassFilters.Clear();
        ClassFilters.Add(new LootFilterClassOption("全部职业", null));
        foreach (var className in Filters
                     .Select(filter => filter.ClassName)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            ClassFilters.Add(new LootFilterClassOption(GetClassLabel(className), className));
        }

        _selectedClass = ClassFilters.FirstOrDefault(option =>
            string.Equals(option.ClassName, selectedClassName, StringComparison.Ordinal))
            ?? ClassFilters[0];
        OnPropertyChanged(nameof(SelectedClass));

        UseCaseFilters.Clear();
        UseCaseFilters.Add(new LootFilterUseCaseOption("全部用途", null));
        foreach (var useCase in Filters
                     .SelectMany(filter => filter.UseCases)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            UseCaseFilters.Add(new LootFilterUseCaseOption(useCase, useCase));
        }

        _selectedUseCase = UseCaseFilters.FirstOrDefault(option =>
            string.Equals(option.UseCase, selectedUseCaseValue, StringComparison.Ordinal))
            ?? UseCaseFilters[0];
        OnPropertyChanged(nameof(SelectedUseCase));
        OnPropertyChanged(nameof(FilterCountText));
        OnPropertyChanged(nameof(HasFilteredFilters));
    }

    private void ApplyFilters()
    {
        OnPropertyChanged(nameof(FilteredFilters));
        OnPropertyChanged(nameof(FilterCountText));
        OnPropertyChanged(nameof(HasFilteredFilters));
        var visible = FilteredFilters.ToList();
        if (visible.Count > 0 && (SelectedFilter is null || !visible.Contains(SelectedFilter)))
        {
            SelectedFilter = visible[0];
        }
    }

    private void SaveLocalFilters()
    {
        _localStore.Save(Filters.Where(filter => !_bundledIds.Contains(filter.Id)).ToArray());
    }

    private static string GetIdentity(LootFilterPreset filter) =>
        string.Join("|", filter.Source, filter.BuildId, filter.VariantIndex, filter.Stage, filter.Name);

    private bool IsMatch(LootFilterPreset filter)
    {
        if (_selectedClass.ClassName is not null
            && !string.Equals(filter.ClassName, _selectedClass.ClassName, StringComparison.Ordinal))
        {
            return false;
        }

        if (_selectedStage.Stage is not null && filter.Stage != _selectedStage.Stage)
        {
            return false;
        }

        if (_selectedUseCase.UseCase is not null
            && !filter.UseCases.Contains(_selectedUseCase.UseCase, StringComparer.Ordinal))
        {
            return false;
        }

        var search = _searchText.Trim();
        if (search.Length == 0)
        {
            return true;
        }

        var searchable = string.Join(" ",
            filter.Name,
            filter.BuildName,
            filter.ClassName,
            filter.StageLabel,
            filter.LevelRange,
            filter.UseCaseLabel,
            filter.Description,
            filter.BuildId);
        return searchable.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset GetUpdatedAt(LootFilterPreset filter) =>
        filter.SourceUpdatedAt ?? filter.UpdatedAt;

    private static int GetStageOrder(LootFilterStage stage) => stage switch
    {
        LootFilterStage.Leveling => 0,
        LootFilterStage.Early => 1,
        LootFilterStage.Mid => 2,
        LootFilterStage.Late => 3,
        LootFilterStage.Push => 4,
        _ => 5
    };

    private static string GetClassLabel(string className) => className switch
    {
        "Druid" => "德鲁伊",
        "Barbarian" => "野蛮人",
        "Rogue" => "游侠",
        "Sorcerer" => "巫师",
        "Necromancer" => "死灵法师",
        "Spiritborn" => "灵巫",
        "Paladin" => "圣骑士",
        "Warlock" => "术士",
        _ => className
    };

    private static List<LootFilterLegend> CreateDefaultLegend() =>
    [
        new() { Color = "#E74C3C", Label = "红色", Description = "可升级威能和需求的暗金" },
        new() { Color = "#E58AD4", Label = "粉色", Description = "3 条可用词条" },
        new() { Color = "#4DA3FF", Label = "蓝色", Description = "2 条正确词条" },
        new() { Color = "#7BD88F", Label = "浅绿色", Description = "可升级威能" }
    ];
}
