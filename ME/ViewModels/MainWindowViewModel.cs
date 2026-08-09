using System.Collections.ObjectModel;
using System.Windows.Input;
using ME.Core;

namespace ME.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private int _currentViewIndex;
        private string _currentViewTitle;

        public int CurrentViewIndex
        {
            get => _currentViewIndex;
            set => SetProperty(ref _currentViewIndex, value);
        }

        public string CurrentViewTitle
        {
            get => _currentViewTitle;
            set => SetProperty(ref _currentViewTitle, value);
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public ICommand NavigateCommand { get; }

        public MainWindowViewModel()
        {
            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem { Name = "任务列表", Icon = "📋", ViewIndex = 0 },
                new NavItem { Name = "目标管理", Icon = "🎯", ViewIndex = 1 },
                new NavItem { Name = "日历视图", Icon = "📅", ViewIndex = 2 },
                new NavItem { Name = "定期盘点", Icon = "📈", ViewIndex = 3 },
                new NavItem { Name = "时间追踪", Icon = "⏱️", ViewIndex = 4 },
                new NavItem { Name = "设置", Icon = "⚙️", ViewIndex = 5 },
            };

            _currentViewTitle = "任务列表";
            NavigateCommand = new RelayCommand(Navigate);
        }

        private void Navigate(object parameter)
        {
            if (parameter is NavItem item)
            {
                CurrentViewIndex = item.ViewIndex;
                CurrentViewTitle = item.Name;
            }
        }
    }

    public class NavItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public int ViewIndex { get; set; }
    }
}
