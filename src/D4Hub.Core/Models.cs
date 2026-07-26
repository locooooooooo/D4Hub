using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace D4Hub.Core;

public enum BuildSectionKind
{
    Skills,
    Paragon,
    Equipment,
    Activities
}

public enum HudDisplayMode
{
    Compact,
    Values
}

public sealed class ChecklistItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isCompleted;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text
    {
        get => _text;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (SetField(ref _text, normalized))
            {
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetField(ref _isCompleted, value);
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(Text);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ChecklistSection : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _description = string.Empty;

    public BuildSectionKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value ?? string.Empty);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value ?? string.Empty);
    }

    public string IconGlyph { get; set; } = "?";
    public ObservableCollection<ChecklistItem> Items { get; set; } = new();

    public int CompletedCount => Items.Count(item => item.IsCompleted);
    public int TotalCount => Items.Count;
    public double CompletionRatio => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyProgressChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletionRatio)));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class BuildPlan : INotifyPropertyChanged
{
    private string _name = "赛季推进构筑";
    private string _className = "Spiritborn";
    private string _season = "Season 11";
    private string _notes = "把网站构筑拆成可执行的小步骤，在游戏外维护自己的进度。";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    public string ClassName
    {
        get => _className;
        set => SetField(ref _className, value ?? string.Empty);
    }

    public string Season
    {
        get => _season;
        set => SetField(ref _season, value ?? string.Empty);
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value ?? string.Empty);
    }

    public ObservableCollection<ChecklistSection> Sections { get; set; } = new();

    public int CompletedCount => Sections.Sum(section => section.CompletedCount);
    public int TotalCount => Sections.Sum(section => section.TotalCount);
    public double CompletionRatio => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyProgressChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletionRatio)));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class OverlaySettings : INotifyPropertyChanged
{
    private double _opacity = 0.94;
    private bool _alwaysOnTop = true;
    private double _left = -1;
    private double _top = -1;
    private bool _autoAttach = true;
    private double _panelConfidenceThreshold = 0.55;
    private double _buildConfidenceThreshold = 0.72;
    private HudDisplayMode _hudDisplayMode = HudDisplayMode.Compact;

    public double Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, Math.Clamp(value, 0.55, 1));
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetField(ref _alwaysOnTop, value);
    }

    public double Left
    {
        get => _left;
        set => SetField(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => SetField(ref _top, value);
    }

    public bool AutoAttach
    {
        get => _autoAttach;
        set => SetField(ref _autoAttach, value);
    }

    public double PanelConfidenceThreshold
    {
        get => _panelConfidenceThreshold;
        set => SetField(ref _panelConfidenceThreshold, Math.Clamp(value, 0.35, 0.95));
    }

    public double BuildConfidenceThreshold
    {
        get => _buildConfidenceThreshold;
        set => SetField(ref _buildConfidenceThreshold, Math.Clamp(value, 0.50, 0.95));
    }

    public HudDisplayMode HudDisplayMode
    {
        get => _hudDisplayMode;
        set => SetField(ref _hudDisplayMode, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class BuildDocument
{
    public int SchemaVersion { get; set; } = 1;
    public BuildPlan Build { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public ObservableCollection<BuildProfile> Profiles { get; set; } = new();
    public string SelectedProfileId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static BuildDocument CreateStarter()
    {
        var starterProfile = HudProfileFactory.CreateStarterProfile();
        return new BuildDocument
        {
            Profiles = new ObservableCollection<BuildProfile> { starterProfile },
            SelectedProfileId = starterProfile.Id,
            Build = new BuildPlan
            {
                Sections = new ObservableCollection<ChecklistSection>
                {
                    new()
                    {
                        Kind = BuildSectionKind.Skills,
                        Key = "skills",
                        Title = "技能与符文",
                        Description = "记录主动技能、被动技能和需要优先升级的关键节点。",
                        IconGlyph = "⚔",
                        Items = new ObservableCollection<ChecklistItem>
                        {
                            new() { Text = "确认核心输出技能与附魔搭配" },
                            new() { Text = "把 5 个主动技能升到目标等级" },
                            new() { Text = "检查被动节点是否满足构筑条件" }
                        }
                    },
                    new()
                    {
                        Kind = BuildSectionKind.Paragon,
                        Key = "paragon",
                        Title = "巅峰盘",
                        Description = "把下一块盘、雕文和关键属性点拆开，逐步完成。",
                        IconGlyph = "◇",
                        Items = new ObservableCollection<ChecklistItem>
                        {
                            new() { Text = "解锁第 2 块巅峰盘" },
                            new() { Text = "把核心雕文升到 15 级" },
                            new() { Text = "确认下一条路径不浪费属性点" }
                        }
                    },
                    new()
                    {
                        Kind = BuildSectionKind.Equipment,
                        Key = "equipment",
                        Title = "装备与威能",
                        Description = "维护需要寻找的词缀与威能，不连接游戏内存。",
                        IconGlyph = "▣",
                        Items = new ObservableCollection<ChecklistItem>
                        {
                            new() { Text = "为武器预留目标威能" },
                            new() { Text = "收集 3 件带核心词缀的装备" },
                            new() { Text = "在城镇完成一次升级与镶嵌" }
                        }
                    },
                    new()
                    {
                        Kind = BuildSectionKind.Activities,
                        Key = "activities",
                        Title = "活动路线",
                        Description = "把本次游戏会话的目标写成几步，完成后逐项打勾。",
                        IconGlyph = "◷",
                        Items = new ObservableCollection<ChecklistItem>
                        {
                            new() { Text = "完成 1 次目标地下城" },
                            new() { Text = "收集低语宝箱并整理背包" },
                            new() { Text = "记录本轮掉落与下一步目标" }
                        }
                    }
                }
            }
        };
    }

    public void EnsureValid()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported D4Hub document schema: {SchemaVersion}");
        }

        Build ??= new BuildPlan();
        Overlay ??= new OverlaySettings();
        Profiles ??= new ObservableCollection<BuildProfile>();
        if (Profiles.Count == 0)
        {
            Profiles.Add(HudProfileFactory.CreateStarterProfile());
        }

        foreach (var profile in Profiles)
        {
            HudProfileFactory.EnsureRules(profile);
        }

        if (Profiles.All(profile => profile.Id != SelectedProfileId))
        {
            SelectedProfileId = Profiles[0].Id;
        }
        Build.Sections ??= new ObservableCollection<ChecklistSection>();
        foreach (var section in Build.Sections)
        {
            section.Items ??= new ObservableCollection<ChecklistItem>();
        }

        if (Build.Sections.Count == 0)
        {
            Build.Sections = CreateStarter().Build.Sections;
        }
    }
}
