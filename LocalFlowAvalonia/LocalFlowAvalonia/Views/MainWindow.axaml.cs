using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LocalFlowAvalonia.ViewModels;

namespace LocalFlowAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var canDrop = DataContext is ShellViewModel { CurrentView: SendViewModel { IsSending: false } }
                      && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
            return;
        if (viewModel.CurrentView is not SendViewModel)
            return;

        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        viewModel.SetDroppedFile(path);
    }
}
