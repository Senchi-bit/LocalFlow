using CommunityToolkit.Mvvm.Input;

namespace LocalFlowAvalonia.ViewModels;

public partial class ModeSelectViewModel : ViewModelBase
{
    private readonly Action _enterSend;
    private readonly Action _enterReceive;

    public ModeSelectViewModel(Action enterSend, Action enterReceive)
    {
        _enterSend = enterSend;
        _enterReceive = enterReceive;
    }

    [RelayCommand]
    private void Send() => _enterSend();

    [RelayCommand]
    private void Receive() => _enterReceive();
}
