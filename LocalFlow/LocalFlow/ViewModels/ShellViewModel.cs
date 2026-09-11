using System.Windows.Threading;

namespace LocalFlow.ViewModels;

internal sealed class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly Dispatcher _ui;
    private object _currentView;
    private string _title = "LocalFlow";

    public ShellViewModel(Dispatcher ui)
    {
        _ui = ui;
        _currentView = new ModeSelectViewModel(EnterSend, EnterReceive);
    }

    public object CurrentView
    {
        get => _currentView;
        private set => Set(ref _currentView, value);
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

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
