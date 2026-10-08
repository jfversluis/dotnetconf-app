using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Pages;

public partial class LicensePage : ContentPage, IQueryAttributable
{
    private readonly ILogger<LicensePage> logger;
    private DependencyNotice? notice;
    public string LicenseTitle { get; private set; } = "License";
    public string LicenseText { get; private set; } = "";
    public bool IsLoading { get; private set; }

    public LicensePage(ILogger<LicensePage> logger)
    {
        this.logger = logger;
        InitializeComponent();
        BindingContext = this;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        notice = query.TryGetValue("notice", out var value) ? value as DependencyNotice : null;
        LicenseTitle = notice?.Name ?? "License";
        OnPropertyChanged(nameof(LicenseTitle));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (LicenseText.Length > 0 || IsLoading)
            return;
        IsLoading = true;
        OnPropertyChanged(nameof(IsLoading));
        try
        {
            if (notice is null)
                throw new InvalidDataException("No dependency notice was supplied.");
            await using var stream = await FileSystem.OpenAppPackageFileAsync(notice.File);
            using var reader = new StreamReader(stream);
            LicenseText = await reader.ReadToEndAsync();
            OnPropertyChanged(nameof(LicenseText));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not read bundled dependency license.");
            LicenseText = "This license could not be loaded. Go back and open it again to retry.";
            OnPropertyChanged(nameof(LicenseText));
            await DisplayAlertAsync("Couldn't load license", LicenseText, "OK");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoading));
        }
    }
}
