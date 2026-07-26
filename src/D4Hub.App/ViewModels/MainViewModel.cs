using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using D4Hub.Core;

namespace D4Hub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IStateStore _stateStore;
    private readonly List<ChecklistItem> _observedItems = new();
    private BuildDocument _document;
    private ChecklistSection? _selectedSection;
    private string _newItemText = string.Empty;
    private string _statusMessage = "本地数据已就绪";

    public MainViewModel(IStateStore stateStore, BuildDocument document)
    {
        _stateStore = stateStore;
        _document = document;
        _document.EnsureValid();
        _selectedSection = _document.Build.Sections.FirstOrDefault();

        AddItemCommand = new RelayCommand(AddItem, () =>
            SelectedSection is not null && !string.IsNullOrWhiteSpace(NewItemText));
        RemoveItemCommand = new RelayCommand<ChecklistItem>(RemoveItem, item => item is not null);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () =>
            SelectedSection?.Items.Any(item => item.IsCompleted) == true);
        SaveCommand = new RelayCommand(() => Save());
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke(this, EventArgs.Empty));
        ToggleOverlayCommand = new RelayCommand(() => ToggleOverlayRequested?.Invoke());
        ShowMainWindowCommand = new RelayCommand(() => ShowMainWindowRequested?.Invoke());

        WireDocument();
    }

    public BuildDocument Document
    {
        get => _document;
        private set
        {
            if (ReferenceEquals(_document, value))
            {
                return;
            }

            UnwireDocument();
            _document = value;
            WireDocument();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Build));
            OnPropertyChanged(nameof(OverallProgressText));
        }
    }

    public BuildPlan Build => Document.Build;

    public ChecklistSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(SelectedProgressText));
                ClearCompletedCommand.RaiseCanExecuteChanged();
                AddItemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewItemText
    {
        get => _newItemText;
        set
        {
            if (SetProperty(ref _newItemText, value))
            {
                AddItemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string OverallProgressText => $"{Build.CompletedCount} / {Build.TotalCount}";

    public string SelectedProgressText => SelectedSection is null
        ? "0 / 0"
        : $"{SelectedSection.CompletedCount} / {SelectedSection.TotalCount}";

    public string StatePath => (_stateStore as JsonStateStore)?.Path ?? "Local state";

    public RelayCommand AddItemCommand { get; }
    public RelayCommand<ChecklistItem> RemoveItemCommand { get; }
    public RelayCommand ClearCompletedCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ToggleOverlayCommand { get; }
    public ICommand ShowMainWindowCommand { get; }

    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;
    public Action? ToggleOverlayRequested { get; set; }
    public Action? ShowMainWindowRequested { get; set; }

    public void Save(bool showStatus = true)
    {
        try
        {
            _stateStore.Save(Document);
            if (showStatus)
            {
                StatusMessage = $"已保存 · {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存失败 · {exception.Message}";
        }
    }

    public void ImportFrom(string path)
    {
        try
        {
            var imported = new JsonStateStore(path).LoadStrict();
            _stateStore.Save(imported);
            Document = imported;
            SelectedSection = Build.Sections.FirstOrDefault();
            StatusMessage = "构筑文件已导入";
        }
        catch (Exception exception)
        {
            StatusMessage = $"导入失败 · {exception.Message}";
        }
    }

    public void ExportTo(string path)
    {
        try
        {
            new JsonStateStore(path).Export(Document, path);
            StatusMessage = "构筑文件已导出";
        }
        catch (Exception exception)
        {
            StatusMessage = $"导出失败 · {exception.Message}";
        }
    }

    public void SetStatus(string message) => StatusMessage = message;

    private void AddItem()
    {
        if (SelectedSection is null || string.IsNullOrWhiteSpace(NewItemText))
        {
            return;
        }

        SelectedSection.Items.Add(new ChecklistItem { Text = NewItemText });
        NewItemText = string.Empty;
        StatusMessage = "已添加清单项";
    }

    private void RemoveItem(ChecklistItem? item)
    {
        if (item is null || SelectedSection is null)
        {
            return;
        }

        if (SelectedSection.Items.Remove(item))
        {
            StatusMessage = "已删除清单项";
        }
    }

    private void ClearCompleted()
    {
        if (SelectedSection is null)
        {
            return;
        }

        var completed = SelectedSection.Items.Where(item => item.IsCompleted).ToArray();
        foreach (var item in completed)
        {
            SelectedSection.Items.Remove(item);
        }

        StatusMessage = completed.Length == 0 ? "没有已完成项目" : $"已清理 {completed.Length} 项";
    }

    private void WireDocument()
    {
        Build.PropertyChanged += BuildPropertyChanged;
        Document.Overlay.PropertyChanged += OverlayPropertyChanged;
        foreach (var section in Build.Sections)
        {
            section.PropertyChanged += SectionPropertyChanged;
            section.Items.CollectionChanged += ItemsCollectionChanged;
        }

        RewireItems();
    }

    private void UnwireDocument()
    {
        Build.PropertyChanged -= BuildPropertyChanged;
        Document.Overlay.PropertyChanged -= OverlayPropertyChanged;
        foreach (var section in Build.Sections)
        {
            section.PropertyChanged -= SectionPropertyChanged;
            section.Items.CollectionChanged -= ItemsCollectionChanged;
        }

        foreach (var item in _observedItems)
        {
            item.PropertyChanged -= ItemPropertyChanged;
        }

        _observedItems.Clear();
    }

    private void RewireItems()
    {
        foreach (var item in _observedItems)
        {
            item.PropertyChanged -= ItemPropertyChanged;
        }

        _observedItems.Clear();
        foreach (var item in Build.Sections.SelectMany(section => section.Items))
        {
            item.PropertyChanged += ItemPropertyChanged;
            _observedItems.Add(item);
        }
    }

    private void BuildPropertyChanged(object? sender, PropertyChangedEventArgs e) => Save(false);

    private void OverlayPropertyChanged(object? sender, PropertyChangedEventArgs e) => Save(false);

    private void SectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChecklistSection.CompletedCount)
            or nameof(ChecklistSection.TotalCount)
            or nameof(ChecklistSection.CompletionRatio))
        {
            return;
        }

        Save(false);
    }

    private void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RewireItems();
        NotifyProgressChanged();
        Save(false);
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ChecklistItem item)
        {
            Build.Sections.FirstOrDefault(section => section.Items.Contains(item))?.NotifyProgressChanged();
        }

        NotifyProgressChanged();
        Save(false);
    }

    private void NotifyProgressChanged()
    {
        Build.NotifyProgressChanged();
        OnPropertyChanged(nameof(OverallProgressText));
        OnPropertyChanged(nameof(SelectedProgressText));
        ClearCompletedCommand.RaiseCanExecuteChanged();
    }
}
