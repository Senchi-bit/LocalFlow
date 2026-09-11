using System.Windows.Input;

namespace LocalFlow.ViewModels;

internal sealed class ModeSelectViewModel : ViewModelBase
{
    public ModeSelectViewModel(Action enterSend, Action enterReceive)
    {
        SendCommand = new RelayCommand(enterSend);
        ReceiveCommand = new RelayCommand(enterReceive);
    }

    public ICommand SendCommand { get; }
    public ICommand ReceiveCommand { get; }
}
