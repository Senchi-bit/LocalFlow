using System.Windows;
using LocalFlow.ViewModels;

namespace LocalFlow;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new ShellViewModel(Dispatcher);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var canDrop = _viewModel.CurrentView is SendViewModel { IsSending: false }
                      && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_viewModel.CurrentView is not SendViewModel)
            return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        _viewModel.SetDroppedFile(files[0]);
    }
}
