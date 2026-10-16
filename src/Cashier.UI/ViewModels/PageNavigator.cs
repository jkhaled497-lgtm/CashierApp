using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public sealed partial class PageNavigator : ObservableObject
{
    public const int DefaultPageSize = 10;
    private Func<int, Task>? loadPage;
    [ObservableProperty] private int pageNumber = 1;
    [ObservableProperty] private int totalPages = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool isLoading;

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public string Summary => $"صفحة {PageNumber} من {TotalPages} · {TotalCount:N0} سجل";
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PreviousPageCommand { get; }
    public IAsyncRelayCommand NextPageCommand { get; }

    public PageNavigator()
    {
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(PageNumber));
        PreviousPageCommand = new AsyncRelayCommand(() => LoadAsync(PageNumber - 1), () => HasPreviousPage);
        NextPageCommand = new AsyncRelayCommand(() => LoadAsync(PageNumber + 1), () => HasNextPage);
    }

    public void Bind(Func<int, Task> pageLoader) => loadPage = pageLoader;

    public void Update(int pageNumber, int totalCount)
    {
        PageNumber = pageNumber;
        TotalCount = totalCount;
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)DefaultPageSize));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(Summary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync(int pageNumber = 1)
    {
        if (loadPage is null || IsLoading)
            return;
        var requestedPage = Math.Clamp(pageNumber, 1, Math.Max(1, TotalPages));
        try
        {
            IsLoading = true;
            await loadPage(requestedPage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnPageNumberChanged(int value)
    {
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(Summary));
}
