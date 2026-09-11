namespace LocalFlowAvalonia.Services;

public interface IUiServices
{
    Task<string?> PickFileAsync(string title);

    Task OpenFolderAsync(string path);
}
