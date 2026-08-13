using Avalonia.Controls;
using RomForge.UI.ViewModels;

namespace RomForge.UI.Views
{
    public partial class ImageDownloadWindow : Window
    {
        public ImageDownloadWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is ImageDownloadWindowVM vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ImageDownloadWindowVM.LogText))
                        ScrollToBottom();
                };
            }
        }

        private void ScrollToBottom()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
                LogTextBox.BringIntoView(new Avalonia.Rect(0, double.MaxValue, 0, 0));
            });
        }
    }
}
