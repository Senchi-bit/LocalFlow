using CommunityToolkit.Mvvm.ComponentModel;
using LocalFlowAvalonia.Services;

namespace LocalFlowAvalonia.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly IUiServices _ui;

    public ShellViewModel(IUiServices ui)
    {
        _ui = ui;
        CurrentView = new ModeSelectViewModel(EnterSend, EnterReceive);
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "LocalFlow";

    public void SetDroppedFile(string path)
    {
        if (CurrentView is SendViewModel send)
            send.SetDroppedFile(path);
    }

    public void Dispose() => DisposeCurrent();

    private void EnterSend()
    {
        DisposeCurrent();
        CurrentView = new SendViewModel(_ui, GoBack);
        Title = "LocalFlow — отправка файла";
    }

    private void EnterReceive()
    {
        DisposeCurrent();
        CurrentView = new ReceiveViewModel(_ui, GoBack);
        Title = "LocalFlow — приём файлов";
    }

    private void GoBack()
    {
        if (CurrentView is SendViewModel { IsSending: true })
            return;

        DisposeCurrent();
        CurrentView = new ModeSelectViewModel(EnterSend, EnterReceive);
        Title = "LocalFlow";
    }

    private void DisposeCurrent()
    {
        if (CurrentView is IDisposable disposable)
            disposable.Dispose();
    }
}
